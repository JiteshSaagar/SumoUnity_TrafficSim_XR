"""
SUMO process and TraCI session lifecycle manager.
"""

import logging
import os
import shutil
import sys
from typing import List, Optional

logger = logging.getLogger("sumo2unity.sumo")


class SumoManager:
    def __init__(
        self,
        sumo_cfg: str,
        use_gui: bool = True,
        step_length: float = 0.10,
        lateral_resolution: float = 0.30,
        free_cam: bool = False,
        zoom: float = 150.0,
        ego_id: str = "f_0.0",
    ):
        self.sumo_cfg = os.path.abspath(sumo_cfg)
        self.use_gui = use_gui
        self.step_length = step_length
        self.lateral_resolution = lateral_resolution
        self.free_cam = free_cam
        self.zoom = zoom
        self.ego_id = ego_id

        self.traci_module = None
        self.is_connected = False
        self._setup_sumo_env()

    def _setup_sumo_env(self) -> None:
        """Ensures SUMO_HOME is configured and tools directory is on sys.path."""
        sumo_home = os.environ.get("SUMO_HOME")
        if not sumo_home:
            potential_paths = [
                r"C:\Program Files (x86)\Eclipse\Sumo",
                r"C:\Program Files\Eclipse\Sumo",
                "/usr/share/sumo",
                "/usr/local/share/sumo",
            ]
            for p in potential_paths:
                if os.path.isdir(p):
                    sumo_home = p
                    os.environ["SUMO_HOME"] = p
                    break

        if sumo_home:
            tools_dir = os.path.join(sumo_home, "tools")
            if os.path.isdir(tools_dir) and tools_dir not in sys.path:
                sys.path.append(tools_dir)
        else:
            logger.warning("SUMO_HOME environment variable is not set.")

        try:
            import traci
            self.traci_module = traci
        except ImportError as err:
            logger.error("Failed to import traci. Ensure SUMO tools are installed: %s", err)
            raise RuntimeError("TraCI Python library not found. Check SUMO_HOME.") from err

    def find_sumo_binary(self) -> str:
        """Finds the path to sumo or sumo-gui executable."""
        binary_name = "sumo-gui" if self.use_gui else "sumo"
        if sys.platform == "win32":
            binary_name += ".exe"

        # Check in SUMO_HOME/bin
        sumo_home = os.environ.get("SUMO_HOME", "")
        candidate = os.path.join(sumo_home, "bin", binary_name)
        if os.path.isfile(candidate):
            return candidate

        # Fallback to PATH search
        which_path = shutil.which(binary_name)
        if which_path:
            return which_path

        raise FileNotFoundError(f"SUMO executable '{binary_name}' could not be found.")

    def start(self) -> None:
        """Launches SUMO and establishes TraCI session."""
        if self.is_connected:
            return

        sumo_bin = self.find_sumo_binary()
        sumo_cmd = [
            sumo_bin,
            "-c",
            self.sumo_cfg,
            "--step-length",
            str(self.step_length),
            "--lateral-resolution",
            str(self.lateral_resolution),
            "--delay",
            "0",
            "--start",
            "--quit-on-end",
        ]

        logger.info("Starting SUMO session with command: %s", " ".join(sumo_cmd))
        self.traci_module.start(sumo_cmd)
        self.is_connected = True

        # Configure GUI tracking if requested
        if self.use_gui and not self.free_cam:
            self._configure_gui_tracking()

    def _configure_gui_tracking(self) -> None:
        """Points the default SUMO view at the ego vehicle if GUI is active."""
        try:
            views = self.traci_module.gui.getIDList()
            if views:
                view_id = views[0]
                self.traci_module.gui.setZoom(view_id, float(self.zoom))
                if self.ego_id in self.traci_module.vehicle.getIDList():
                    self.traci_module.gui.trackVehicle(view_id, self.ego_id)
        except Exception as err:
            logger.debug("Could not configure initial GUI camera tracking: %s", err)

    def update_gui_camera(self) -> None:
        """Ensures camera stays locked to ego vehicle as it enters the simulation."""
        if not self.use_gui or self.free_cam or not self.is_connected:
            return
        try:
            views = self.traci_module.gui.getIDList()
            if views:
                view_id = views[0]
                if self.ego_id in self.traci_module.vehicle.getIDList():
                    self.traci_module.gui.trackVehicle(view_id, self.ego_id)
        except Exception:
            pass

    def close(self) -> None:
        """Closes TraCI connection and shuts down SUMO."""
        if self.is_connected and self.traci_module:
            try:
                self.traci_module.close()
            except Exception as err:
                logger.debug("TraCI close exception: %s", err)
            finally:
                self.is_connected = False
        logger.info("SUMO session closed.")
