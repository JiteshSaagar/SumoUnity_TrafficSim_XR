"""
Configuration classes and defaults for Sumo2Unity.
"""

from dataclasses import dataclass, field
import os
from typing import List, Optional


@dataclass
class SimulationConfig:
    sumo_cfg: str = ""
    integration_start_time: float = 540.0
    experiment_start_time: float = 600.0
    experiment_end_time: float = 720.0
    step_length: float = 0.10
    lateral_resolution: float = 0.30
    zoom: float = 150.0
    subscribe_radius: float = 250.0
    ego_id: str = "f_0.0"
    # When the ego is the VR pedestrian, it must be injected into SUMO as a
    # person rather than a vehicle. ego_id then names the person, not a trip
    # from the route file.
    ego_is_pedestrian: bool = False
    ego_person_type: str = "ped_xr"
    use_gui: bool = True
    calc_rtf: bool = True
    free_cam: bool = False
    pub_port: int = 5556
    router_port: int = 5557
    results_dir: str = ""
    unity_step_interval: float = 0.10
    tl_update_interval: float = 0.20

    def validate(self) -> List[str]:
        errors = []
        if not self.sumo_cfg:
            errors.append("SUMO configuration (.sumocfg) file path is required.")
        elif not os.path.isfile(self.sumo_cfg):
            errors.append(f"SUMO configuration file does not exist: {self.sumo_cfg}")

        if self.step_length <= 0:
            errors.append("Step length must be greater than 0.")
        if self.experiment_end_time <= self.experiment_start_time:
            errors.append("Experiment End Time must be greater than Experiment Start Time.")
        if self.subscribe_radius <= 0:
            errors.append("Subscribe radius must be greater than 0.")
        return errors


def find_project_root(start_dir: Optional[str] = None) -> str:
    """Finds the workspace root by looking for Assets or SumoUnity_TrafficSim_XR.sln."""
    current = os.path.abspath(start_dir or os.getcwd())
    while current:
        if os.path.isdir(os.path.join(current, "Assets")) or os.path.isfile(os.path.join(current, "SumoUnity_TrafficSim_XR.sln")):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
    return os.path.abspath(start_dir or os.getcwd())


def locate_results_dir(project_root: Optional[str] = None) -> str:
    root = project_root or find_project_root()
    results_path = os.path.join(root, "Results")
    os.makedirs(results_path, exist_ok=True)
    return results_path


def find_available_scenarios(project_root: Optional[str] = None) -> List[str]:
    """Finds all .sumocfg files in the project."""
    root = project_root or find_project_root()
    scenarios = []
    for dirpath, _, filenames in os.walk(root):
        # Ignore Library, Packages, .git, Temp
        if any(ign in dirpath for ign in ["Library", "Packages", ".git", "Temp", "UserSettings"]):
            continue
        for f in filenames:
            if f.endswith(".sumocfg"):
                scenarios.append(os.path.join(dirpath, f))
    return scenarios
