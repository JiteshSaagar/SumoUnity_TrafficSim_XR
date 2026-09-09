"""
High-precision synchronization engine between SUMO and Unity.
Executes the co-simulation loop, context subscriptions, and real-time pacing.
"""

import logging
import math
import os
import time
from typing import Any, Callable, Dict, List, Optional

from ..analytics.metrics_logger import MetricsLogger
from ..config import SimulationConfig
from ..network.zmq_bridge import ZMQBridge
from .actors import ActorManager
from .sumo_manager import SumoManager

logger = logging.getLogger("sumo2unity.engine")


def classify_person_road(road_id: str) -> str:
    """
    Maps a SUMO road id to the surface a pedestrian is standing on.

    Internal edges are prefixed with ':' and suffixed by function: '_c' for a
    crossing, '_w' for a walking area. Anything else is a normal edge, i.e. the
    sidewalk lane. Unity uses this to pick an animation and, later, to score
    crossing analytics without re-deriving it from geometry.
    """
    if road_id.startswith(":"):
        if "_c" in road_id:
            return "crossing"
        if "_w" in road_id:
            return "walkingarea"
        return "internal"
    return "sidewalk"


class SyncEngine:
    def __init__(
        self,
        config: SimulationConfig,
        on_step_callback: Optional[Callable[[Dict[str, Any]], None]] = None,
    ):
        self.config = config
        self.on_step_callback = on_step_callback

        self.sumo_manager = SumoManager(
            sumo_cfg=config.sumo_cfg,
            use_gui=config.use_gui,
            step_length=config.step_length,
            lateral_resolution=config.lateral_resolution,
            free_cam=config.free_cam,
            zoom=config.zoom,
            ego_id=config.ego_id,
        )
        self.zmq_bridge = ZMQBridge(
            pub_port=config.pub_port,
            router_port=config.router_port,
        )
        self.actor_manager = ActorManager(
            primary_ego_id=config.ego_id,
            person_type=config.ego_person_type,
        )
        self.metrics_logger = MetricsLogger(results_dir=config.results_dir)

        self.is_running = False
        self.should_stop = False
        self.start_rec_sent = False
        self.stop_rec_sent = False

        self.last_tl_update = 0.0
        self.last_pos_z: Dict[str, float] = {}

    def start(self) -> None:
        """Starts all subsystems and executes the co-simulation loop."""
        logger.info("Starting Sumo2Unity Co-Simulation Engine...")
        self.is_running = True
        self.should_stop = False
        self.start_rec_sent = False
        self.stop_rec_sent = False

        try:
            self.zmq_bridge.start()
            self.sumo_manager.start()
            self.metrics_logger.start()

            self._run_loop()

        except KeyboardInterrupt:
            logger.info("Simulation interrupted by user.")
        except Exception as err:
            logger.exception("Error during simulation: %s", err)
            raise
        finally:
            self.stop()

    def _sleep_precise(self, target_duration: float) -> None:
        """High-precision sleep using busy-wait polling for the last few milliseconds."""
        if target_duration <= 0:
            return
        t0 = time.perf_counter()
        while (time.perf_counter() - t0) < target_duration:
            rem = target_duration - (time.perf_counter() - t0)
            if rem > 0.002:
                time.sleep(0.001)

    def _run_loop(self) -> None:
        """Main co-simulation stepping loop."""
        traci = self.sumo_manager.traci_module
        step_len = self.config.step_length

        logger.info("Entering co-simulation loop...")

        while self.is_running and not self.should_stop:
            loop_t0 = time.perf_counter()

            # 1. Check if simulation has completed or vehicles remaining
            min_expected = traci.simulation.getMinExpectedNumber()
            if min_expected <= 0:
                logger.info("SUMO simulation completed: no further expected vehicles.")
                break

            current_sim_time = traci.simulation.getTime()

            # 2. Ingest Unity Telemetry
            unity_data = self.zmq_bridge.get_latest_unity_data()
            if unity_data:
                self.actor_manager.update_from_unity(unity_data)
                self.actor_manager.sync_to_sumo(traci)

            # 3. Step SUMO
            traci.simulationStep()
            self.sumo_manager.update_gui_camera()

            # 4. Handle Recording Trigger Milestones
            if current_sim_time >= self.config.integration_start_time and not self.start_rec_sent:
                self.zmq_bridge.send_command("START_RECORDING")
                self.start_rec_sent = True
                logger.info("Triggered START_RECORDING at sim time %.2f", current_sim_time)

            if current_sim_time >= self.config.experiment_end_time and not self.stop_rec_sent:
                self.zmq_bridge.send_command("STOP_RECORDING")
                self.stop_rec_sent = True
                logger.info("Triggered STOP_RECORDING at sim time %.2f", current_sim_time)
                break

            # 5. Extract Surrounding Vehicles
            all_veh_ids = traci.vehicle.getIDList()
            vehicles_to_send = []

            # Determine Ego position for radius filtering. A pedestrian ego is a
            # SUMO person, so looking it up in the vehicle list would always miss
            # and the radius filter would silently fall back to "send everything".
            ego_pos = None
            if self.config.ego_is_pedestrian:
                try:
                    if self.config.ego_id in traci.person.getIDList():
                        ego_pos = traci.person.getPosition(self.config.ego_id)
                except Exception:
                    pass
            elif self.config.ego_id in all_veh_ids:
                try:
                    ego_pos = traci.vehicle.getPosition(self.config.ego_id)
                except Exception:
                    pass

            if ego_pos is None and self.config.ego_id in self.actor_manager.active_actors:
                # Before SUMO has the actor, fall back to what Unity last sent.
                ego_actor = self.actor_manager.active_actors[self.config.ego_id]
                ego_pos = (ego_actor.x, ego_actor.y)

            sub_radius_sq = self.config.subscribe_radius ** 2

            for vid in all_veh_ids:
                try:
                    p3d = traci.vehicle.getPosition3D(vid)
                    # Radius check relative to Ego
                    if ego_pos:
                        dx = p3d[0] - ego_pos[0]
                        dy = p3d[1] - ego_pos[1]
                        if (dx * dx + dy * dy) > sub_radius_sq:
                            continue

                    ang = traci.vehicle.getAngle(vid)
                    vtype = traci.vehicle.getTypeID(vid)
                    vlong = traci.vehicle.getSpeed(vid)
                    vlat = traci.vehicle.getLateralSpeed(vid)

                    # Compute vertical speed from delta Z
                    prev_z = self.last_pos_z.get(vid, p3d[2])
                    vvert = (p3d[2] - prev_z) / step_len if step_len > 0 else 0.0
                    self.last_pos_z[vid] = p3d[2]

                    vehicles_to_send.append({
                        "vehicle_id": vid,
                        "position": [round(p3d[0], 2), round(p3d[1], 2), round(p3d[2], 2)],
                        "angle": round(ang, 2),
                        "type": vtype,
                        "long_speed": round(vlong, 2),
                        "vert_speed": round(vvert, 2),
                        "lat_speed": round(vlat, 2),
                    })
                except Exception as err:
                    logger.debug("Error reading vehicle %s: %s", vid, err)

            # Publish vehicles to Unity
            if vehicles_to_send:
                self.zmq_bridge.send_vehicles(vehicles_to_send)

            # 5b. Extract Pedestrians (SUMO -> Unity)
            persons_to_send = []
            all_person_ids = traci.person.getIDList()
            for pid in all_person_ids:
                try:
                    # Never send the XR subject back: Unity owns that pose, and
                    # echoing it would spawn an NPC standing inside the headset.
                    if self.config.ego_is_pedestrian and pid == self.config.ego_id:
                        continue

                    pp = traci.person.getPosition3D(pid)
                    # Same radius filter as vehicles, so a pedestrian on the far
                    # side of the city costs nothing.
                    if ego_pos:
                        dx = pp[0] - ego_pos[0]
                        dy = pp[1] - ego_pos[1]
                        if (dx * dx + dy * dy) > sub_radius_sq:
                            continue

                    persons_to_send.append({
                        "person_id": pid,
                        "position": [round(pp[0], 2), round(pp[1], 2), round(pp[2], 2)],
                        "angle": round(traci.person.getAngle(pid), 2),
                        "type": traci.person.getTypeID(pid),
                        "speed": round(traci.person.getSpeed(pid), 2),
                        "road_id": traci.person.getRoadID(pid),
                        "state": classify_person_road(traci.person.getRoadID(pid)),
                    })
                except Exception as err:
                    logger.debug("Error reading person %s: %s", pid, err)

            if persons_to_send:
                self.zmq_bridge.send_persons(persons_to_send)

            # 6. Extract Traffic Lights (periodic update)
            now_wall = time.time()
            if (now_wall - self.last_tl_update) >= self.config.tl_update_interval:
                tls_data = []
                try:
                    for tl_id in traci.trafficlight.getIDList():
                        state = traci.trafficlight.getRedYellowGreenState(tl_id)
                        tls_data.append({"junction_id": tl_id, "state": state})
                except Exception as err:
                    logger.debug("Error reading traffic lights: %s", err)

                if tls_data:
                    self.zmq_bridge.send_traffic_lights(tls_data)
                self.last_tl_update = now_wall

            # 7. Record Metrics
            current_rtf = 1.0
            if self.config.calc_rtf and self.start_rec_sent:
                current_rtf = self.metrics_logger.record_step(current_sim_time)

            # 8. Notify UI Callback
            if self.on_step_callback:
                ego_speed = 0.0
                if self.config.ego_id in self.actor_manager.active_actors:
                    ego_speed = self.actor_manager.active_actors[self.config.ego_id].long_speed

                self.on_step_callback({
                    "sim_time": current_sim_time,
                    "active_vehicles": len(vehicles_to_send),
                    "total_sumo_vehicles": len(all_veh_ids),
                    "active_persons": len(persons_to_send),
                    "total_sumo_persons": len(all_person_ids),
                    "rtf": current_rtf,
                    "ego_speed": ego_speed,
                    "unity_connected": self.zmq_bridge.is_unity_connected(),
                    "recording": self.start_rec_sent and not self.stop_rec_sent,
                })

            # 9. Pacing (Real-Time Sleep)
            elapsed = time.perf_counter() - loop_t0
            target_sleep = step_len - elapsed
            self._sleep_precise(target_sleep)

    def stop(self) -> None:
        """Stops the simulation and closes connections."""
        self.should_stop = True
        self.is_running = False

        if not self.stop_rec_sent and self.zmq_bridge.is_running:
            self.zmq_bridge.send_command("STOP_RECORDING")
            self.stop_rec_sent = True

        self.sumo_manager.close()
        self.zmq_bridge.close()
        self.metrics_logger.close()
        logger.info("Co-Simulation Engine stopped successfully.")
