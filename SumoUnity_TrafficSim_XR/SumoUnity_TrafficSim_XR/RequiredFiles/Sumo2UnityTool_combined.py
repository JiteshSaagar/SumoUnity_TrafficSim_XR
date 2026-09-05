#!/usr/bin/env python3
# ────────────────────────────────────────────────────────────────
#  Sumo2UnityTool_combined.py
#  GUI  +  SUMO ⇄ Unity simulation  (one file entry point)
#  Version : Sumo2Unity v2.1.0
#  Author  : Ahmad Mohammadi, PhD – York University
#  License : MIT
# ────────────────────────────────────────────────────────────────

import argparse
import logging
import os
import sys

# Ensure this script's directory is in sys.path
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from sumo2unity.config import SimulationConfig, find_available_scenarios, find_project_root, locate_results_dir
from sumo2unity.core.sync_engine import SyncEngine
from sumo2unity.gui.app import launch_gui

VERSION = "Sumo2Unity v2.1.0"
LINKEDIN_URL = "https://www.linkedin.com/in/ahmadmohammadi1441/"


def setup_logging(debug: bool = False) -> None:
    level = logging.DEBUG if debug else logging.INFO
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)s] (%(name)s) %(message)s",
        datefmt="%H:%M:%S",
    )


def run_cli(args: argparse.Namespace) -> None:
    project_root = find_project_root()
    results_dir = args.results_dir or locate_results_dir(project_root)

    cfg_path = args.config
    if not cfg_path:
        scenarios = find_available_scenarios(project_root)
        if scenarios:
            cfg_path = scenarios[0]
            print(f"[CLI] Auto-selected scenario: {cfg_path}")
        else:
            print("[CLI] Error: No .sumocfg file specified and none found automatically.")
            sys.exit(1)

    config = SimulationConfig(
        sumo_cfg=os.path.abspath(cfg_path),
        integration_start_time=args.integration_start,
        experiment_start_time=args.experiment_start,
        experiment_end_time=args.experiment_end,
        step_length=args.step_length,
        lateral_resolution=args.lateral_res,
        zoom=args.zoom,
        subscribe_radius=args.radius,
        use_gui=not args.no_sumo_gui,
        calc_rtf=not args.no_rtf,
        free_cam=args.free_cam,
        pub_port=args.pub_port,
        router_port=args.router_port,
        results_dir=results_dir,
    )

    errors = config.validate()
    if errors:
        print("[CLI] Validation errors:")
        for err in errors:
            print(f"  - {err}")
        sys.exit(1)

    print(f"=== {VERSION} CLI Runner ===")
    print(f"Scenario:          {config.sumo_cfg}")
    print(f"Integration Start: {config.integration_start_time}s")
    print(f"Experiment Range:  {config.experiment_start_time}s -> {config.experiment_end_time}s")
    print(f"Step Length:       {config.step_length}s")
    print(f"ZMQ Ports:         PUB={config.pub_port}, ROUTER={config.router_port}")
    print(f"Results Directory: {config.results_dir}")
    print("================================")

    def on_step(stats: dict):
        if int(stats['sim_time'] * 10) % 20 == 0:  # Print every 2 simulation seconds
            status_str = "CONNECTED" if stats['unity_connected'] else "WAITING"
            rec_str = "RECORDING" if stats['recording'] else "IDLE"
            print(f"Time: {stats['sim_time']:6.1f}s | Vehicles: {stats['active_vehicles']:3d} | RTF: {stats['rtf']:4.2f}x | Unity: {status_str} | Rec: {rec_str}")

    engine = SyncEngine(config=config, on_step_callback=on_step)
    try:
        engine.start()
    except KeyboardInterrupt:
        print("\n[CLI] Interrupted by user.")
    finally:
        engine.stop()
        print("[CLI] Run finished.")


def main() -> None:
    parser = argparse.ArgumentParser(description=f"{VERSION} - SUMO <-> Unity Co-Simulation Backend")
    parser.add_argument("--headless", "--cli", action="store_true", help="Run in headless CLI mode without GUI")
    parser.add_argument("--config", "-c", type=str, default="", help="Path to .sumocfg file")
    parser.add_argument("--integration-start", type=float, default=540.0, help="Time in seconds to trigger START_RECORDING")
    parser.add_argument("--experiment-start", type=float, default=600.0, help="Experiment start milestone time")
    parser.add_argument("--experiment-end", type=float, default=720.0, help="Time in seconds to stop simulation")
    parser.add_argument("--step-length", type=float, default=0.10, help="SUMO simulation step length in seconds")
    parser.add_argument("--lateral-res", type=float, default=0.30, help="Sublane lateral resolution in meters")
    parser.add_argument("--radius", type=float, default=250.0, help="Subscription radius around ego vehicle")
    parser.add_argument("--zoom", type=float, default=150.0, help="SUMO-GUI camera zoom level")
    parser.add_argument("--no-sumo-gui", action="store_true", help="Launch SUMO without GUI window")
    parser.add_argument("--no-rtf", action="store_true", help="Disable RTF performance calculation and logging")
    parser.add_argument("--free-cam", action="store_true", help="Do not track ego vehicle with SUMO camera")
    parser.add_argument("--pub-port", type=int, default=5556, help="ZeroMQ PUB port (Unity subscriber)")
    parser.add_argument("--router-port", type=int, default=5557, help="ZeroMQ ROUTER port (Unity dealer)")
    parser.add_argument("--results-dir", type=str, default="", help="Directory to output reports")
    parser.add_argument("--debug", action="store_true", help="Enable debug logging")

    args = parser.parse_args()
    setup_logging(args.debug)

    # If headless was explicitly requested or arguments passed without a display
    if args.headless or args.config:
        run_cli(args)
    else:
        # Launch modern GUI
        try:
            launch_gui()
        except Exception as ex:
            logger.warning("Could not launch GUI (%s). Falling back to CLI mode.", ex)
            run_cli(args)


if __name__ == "__main__":
    main()
