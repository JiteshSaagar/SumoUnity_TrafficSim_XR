"""
Fast JSON serialization and deserialization helpers for Unity ⇄ SUMO communication.
"""

import json
from typing import Any, Dict, List, Optional, Tuple

try:
    import orjson

    def dumps(obj: Any) -> str:
        return orjson.dumps(obj).decode("utf-8")

    def loads(raw: str | bytes) -> Any:
        return orjson.loads(raw)

except ImportError:
    def dumps(obj: Any) -> str:
        return json.dumps(obj, separators=(",", ":"))

    def loads(raw: str | bytes) -> Any:
        if isinstance(raw, bytes):
            raw = raw.decode("utf-8")
        return json.loads(raw)


def build_command_message(command: str) -> str:
    """Builds a command JSON string: {"type": "command", "command": "<CMD>"}."""
    return dumps({"type": "command", "command": command})


def build_vehicles_message(vehicles_list: List[Dict[str, Any]]) -> str:
    """
    Builds vehicle array JSON string compatible with Unity's VehicleWrapper:
    {"type": "vehicles", "vehicles": [...]}
    """
    return dumps({"type": "vehicles", "vehicles": vehicles_list})


def build_persons_message(persons_list: List[Dict[str, Any]]) -> str:
    """
    Builds pedestrian array JSON string compatible with Unity's PersonWrapper:
    {"type": "persons", "persons": [...]}

    Kept separate from the vehicles message so Unity can spawn pedestrians from
    a different prefab list and never runs a person through VehicleController.
    """
    return dumps({"type": "persons", "persons": persons_list})


def build_traffic_lights_message(lights_list: List[Dict[str, Any]]) -> str:
    """
    Builds traffic light array JSON string compatible with Unity's TrafficLightsWrapper:
    {"type": "trafficlights", "lights": [{"junction_id": "...", "state": "..."}]}
    """
    return dumps({"type": "trafficlights", "lights": lights_list})


def parse_unity_message(raw_msg: str | bytes) -> Optional[List[Dict[str, Any]]]:
    """
    Parses JSON received from Unity SimulationController.GetVehicleDataJson().
    Expected structure: {"vehicles": [{ "vehicle_id": "...", "position": [x, y, z], ... }]}
    """
    try:
        data = loads(raw_msg)
        if isinstance(data, dict):
            return data.get("vehicles", [])
        elif isinstance(data, list):
            return data
        return None
    except Exception:
        return None
