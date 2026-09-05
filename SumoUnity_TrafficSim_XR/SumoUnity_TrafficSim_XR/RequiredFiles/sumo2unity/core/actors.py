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

    def __init__(self, primary_ego_id: str = "f_0.0"):
        self.primary_ego_id = primary_ego_id
        self.active_actors: Dict[str, ActorState] = {}

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
                    if actor_id in active_person_ids:
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
