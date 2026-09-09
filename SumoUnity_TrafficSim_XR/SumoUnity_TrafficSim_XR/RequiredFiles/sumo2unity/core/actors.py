"""
Actor definitions, coordinate conversions, and multi-ego management.
"""

from dataclasses import dataclass
import logging
from typing import Any, Dict, List, Optional, Tuple

logger = logging.getLogger("sumo2unity.actors")


@dataclass
class ActorState:
    actor_id: str
    x: float
    y: float
    z: float
    angle: float
    long_speed: float
    lat_speed: float
    vert_speed: float
    actor_type: str = "ego"  # "ego", "pedestrian", "bike", "scooter"


class ActorManager:
    """Manages injection of Unity actors (cars, bikes, scooters, pedestrians) into SUMO."""

    def __init__(self, primary_ego_id: str = "f_0.0", person_type: str = "ped_xr"):
        self.primary_ego_id = primary_ego_id
        self.person_type = person_type
        self.active_actors: Dict[str, ActorState] = {}
        # Ids we failed to inject, so a pedestrian standing off the network does
        # not spam a create attempt (and a warning) on every single step.
        self._person_create_failures: Dict[str, int] = {}

    def update_from_unity(self, unity_vehicles_list: List[Dict[str, Any]]) -> None:
        """Parses vehicle telemetry list received from Unity."""
        for v in unity_vehicles_list:
            v_id = v.get("vehicle_id")
            if not v_id:
                continue

            pos = v.get("position", [0.0, 0.0, 0.0])
            angle = float(v.get("angle", 0.0))
            long_speed = float(v.get("long_speed", 0.0))
            lat_speed = float(v.get("lat_speed", 0.0))
            vert_speed = float(v.get("vert_speed", 0.0))
            actor_type = v.get("type", "ego")

            # In Unity: position[0] = X, position[1] = SUMO_Y (Unity Z), position[2] = SUMO_Z (Unity Y)
            state = ActorState(
                actor_id=v_id,
                x=float(pos[0]),
                y=float(pos[1]),
                z=float(pos[2]),
                angle=angle,
                long_speed=long_speed,
                lat_speed=lat_speed,
                vert_speed=vert_speed,
                actor_type=actor_type,
            )
            self.active_actors[v_id] = state

    def sync_to_sumo(self, traci_module: Any) -> None:
        """Applies actor positions to SUMO via moveToXY."""
        active_veh_ids = set(traci_module.vehicle.getIDList())
        active_person_ids = set(traci_module.person.getIDList())

        for actor_id, state in self.active_actors.items():
            try:
                # Convert Unity angle to SUMO heading
                # Unity: 0 = Z (North), 90 = X (East)
                # SUMO: 0 = North, 90 = East, 180 = South
                sumo_angle = state.angle

                if state.actor_type == "pedestrian":
                    if actor_id not in active_person_ids:
                        # The person does not exist yet (first frame from Unity,
                        # or SUMO dropped it). Inject it, then let the next step
                        # drive it; on the creation step it already sits at the
                        # right place.
                        if self._create_person(traci_module, state):
                            active_person_ids.add(actor_id)
                        continue

                    traci_module.person.moveToXY(
                        personID=actor_id,
                        edgeID="",
                        x=state.x,
                        y=state.y,
                        angle=sumo_angle,
                        keepRoute=2,
                    )
                else:
                    if actor_id in active_veh_ids:
                        # keepRoute=2 allows vehicle to move freely across edges
                        traci_module.vehicle.moveToXY(
                            vehID=actor_id,
                            edgeID="",
                            lane=-1,
                            x=state.x,
                            y=state.y,
                            angle=sumo_angle,
                            keepRoute=2,
                        )
            except Exception as err:
                logger.debug("Failed to moveToXY for actor %s: %s", actor_id, err)

    def _create_person(self, traci_module: Any, state: ActorState) -> bool:
        """
        Injects an externally driven pedestrian (the VR subject) into SUMO.

        SUMO has no "free-floating person" concept: a person must be created on
        an edge, and a person with no remaining stage is removed at the end of
        the step it was created in. So a one-metre walking stage is appended
        purely as a keep-alive. It is never actually walked, because moveToXY
        overrides the position every step - verified holding a person alive for
        120 s of continuous moveToXY with the stage count never dropping to 0.

        Returns True if the person now exists in SUMO.
        """
        try:
            edge_id, lane_pos, _lane_index = traci_module.simulation.convertRoad(
                state.x, state.y, False, "pedestrian"
            )
        except Exception as err:
            self._note_create_failure(state.actor_id, "convertRoad failed: %s" % err)
            return False

        if not edge_id:
            self._note_create_failure(
                state.actor_id,
                "no pedestrian edge near (%.1f, %.1f)" % (state.x, state.y),
            )
            return False

        try:
            traci_module.person.add(
                personID=state.actor_id,
                edgeID=edge_id,
                pos=lane_pos,
                typeID=self.person_type,
            )
            traci_module.person.appendWalkingStage(
                personID=state.actor_id, edges=[edge_id], arrivalPos=1.0
            )
        except Exception as err:
            self._note_create_failure(state.actor_id, "person.add failed: %s" % err)
            return False

        logger.info(
            "Injected XR pedestrian '%s' as SUMO person (type=%s) on edge %s at pos %.2f",
            state.actor_id, self.person_type, edge_id, lane_pos,
        )
        self._person_create_failures.pop(state.actor_id, None)
        return True

    def _note_create_failure(self, actor_id: str, reason: str) -> None:
        """Logs the first failure per actor at INFO, the rest at DEBUG."""
        count = self._person_create_failures.get(actor_id, 0)
        self._person_create_failures[actor_id] = count + 1
        if count == 0:
            logger.info("Cannot inject pedestrian '%s' yet: %s", actor_id, reason)
        else:
            logger.debug("Still cannot inject pedestrian '%s': %s", actor_id, reason)
