"""
Modern Dark-Themed GUI Dashboard for Sumo2Unity Co-Simulation.
"""

import logging
import os
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from typing import List, Optional
from PIL import Image, ImageTk

from ..config import SimulationConfig, find_available_scenarios, find_project_root, locate_results_dir
from ..core.sync_engine import SyncEngine

logger = logging.getLogger("sumo2unity.gui")

VERSION = "SumoUnity_TrafficSim_XR v2.1"
AUTHOR = "Jitesh Surendra Saagar"
AFFILIATION = "Intern under Dr. Anshuman Sharma, IIT BHU"
PORTFOLIO_URL = "https://jiteshsaagar.me"


class Sumo2UnityApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title(f"{VERSION} – SUMO ⇄ Unity Co-Simulation Bridge")
        self.root.geometry("860x780")
        self.root.minsize(800, 720)
        self.root.configure(bg="#18181b")  # Modern dark zinc background

        self.project_root = find_project_root()
        self.results_dir = locate_results_dir(self.project_root)

        self.engine: Optional[SyncEngine] = None
        self.sim_thread: Optional[threading.Thread] = None

        # Banner image slideshow
        self.banner_images: List[ImageTk.PhotoImage] = []
        self.current_banner_idx = 0
        self.banner_timer_id = None

        # UI State Variables
        self.sumo_cfg_var = tk.StringVar()
        self.integration_start_var = tk.StringVar(value="540.0")
        self.experiment_start_var = tk.StringVar(value="600.0")
        self.experiment_end_var = tk.StringVar(value="720.0")
        self.steplength_var = tk.StringVar(value="0.10")
        self.lateral_res_var = tk.StringVar(value="0.30")
        self.zoom_var = tk.StringVar(value="150.0")
        self.radius_var = tk.StringVar(value="250.0")

        self.use_gui_var = tk.BooleanVar(value=True)
        self.calc_rtf_var = tk.BooleanVar(value=True)
        self.free_cam_var = tk.BooleanVar(value=False)

        # Telemetry display variables
        self.stat_sim_time = tk.StringVar(value="0.0 s")
        self.stat_vehicles = tk.StringVar(value="0 in range")
        self.stat_rtf = tk.StringVar(value="1.00x")
        self.stat_speed = tk.StringVar(value="0.0 km/h")
        self.stat_unity_status = tk.StringVar(value="Waiting for Unity...")
        self.stat_recording = tk.StringVar(value="IDLE")

        self._init_styles()
        self._load_banner_assets()
        self._build_ui()
        self._auto_detect_scenario()

    def _init_styles(self) -> None:
        style = ttk.Style(self.root)
        style.theme_use("clam")

        # Configure custom modern dark widgets
        style.configure("TFrame", background="#18181b")
        style.configure("Card.TFrame", background="#27272a", relief="flat")

        style.configure(
            "TLabel",
            background="#18181b",
            foreground="#f4f4f5",
            font=("Segoe UI", 10),
        )
        style.configure(
            "CardLabel.TLabel",
            background="#27272a",
            foreground="#d4d4d8",
            font=("Segoe UI", 9),
        )
        style.configure(
            "CardVal.TLabel",
            background="#27272a",
            foreground="#38bdf8",
            font=("Segoe UI", 14, "bold"),
        )
        style.configure(
            "Header.TLabel",
            background="#18181b",
            foreground="#fafafa",
            font=("Segoe UI", 13, "bold"),
        )
        style.configure(
            "TCheckbutton",
            background="#27272a",
            foreground="#f4f4f5",
            font=("Segoe UI", 9),
        )

        style.configure(
            "Start.TButton",
            background="#22c55e",
            foreground="#ffffff",
            font=("Segoe UI", 11, "bold"),
            padding=(15, 8),
            borderwidth=0,
        )
        style.map("Start.TButton", background=[("active", "#16a34a"), ("disabled", "#52525b")])

        style.configure(
            "Stop.TButton",
            background="#ef4444",
            foreground="#ffffff",
            font=("Segoe UI", 11, "bold"),
            padding=(15, 8),
            borderwidth=0,
        )
        style.map("Stop.TButton", background=[("active", "#dc2626"), ("disabled", "#52525b")])

        style.configure(
            "Secondary.TButton",
            background="#3f3f46",
            foreground="#ffffff",
            font=("Segoe UI", 9),
            padding=(8, 4),
        )
        style.map("Secondary.TButton", background=[("active", "#52525b")])

    def _load_banner_assets(self) -> None:
        """Finds and loads slideshow banner images from Assets/_Project/Icons/."""
        candidate_dirs = [
            os.path.join(self.project_root, "Assets", "_Project", "Icons"),
            os.path.join(self.project_root, "RequiredFiles"),
            os.path.dirname(os.path.abspath(__file__)),
        ]

        target_names = ["2.Integration.png", "2.Integration_B.png", "2.Integration.JPG", "2.Integration_B.JPG"]
        found_paths = []
        for cdir in candidate_dirs:
            for name in target_names:
                p = os.path.join(cdir, name)
                if os.path.isfile(p) and p not in found_paths:
                    found_paths.append(p)

        target_width = 820
        target_height = 160
        for p in found_paths:
            try:
                img = Image.open(p)
                img = img.resize((target_width, target_height), Image.Resampling.LANCZOS)
                self.banner_images.append(ImageTk.PhotoImage(img))
            except Exception as e:
                logger.debug("Could not load banner %s: %s", p, e)

    def _build_ui(self) -> None:
        main_container = ttk.Frame(self.root, padding=12)
        main_container.pack(fill="both", expand=True)

        # 1. Top Banner Section
        if self.banner_images:
            self.banner_label = tk.Label(main_container, image=self.banner_images[0], bg="#18181b", bd=0)
            self.banner_label.pack(fill="x", pady=(0, 10))
            self._start_banner_slideshow()
        else:
            title_card = ttk.Frame(main_container, style="Card.TFrame", padding=12)
            title_card.pack(fill="x", pady=(0, 10))
            ttk.Label(title_card, text=f"🚗 {VERSION} – SUMO ⇄ Unity Bridge", style="Header.TLabel").pack()
            ttk.Label(title_card, text="High Performance Co-Simulation for XR Digital Twins", style="CardLabel.TLabel").pack()

        # 2. Scenario File Selector Card
        scenario_card = ttk.Frame(main_container, style="Card.TFrame", padding=10)
        scenario_card.pack(fill="x", pady=(0, 10))

        ttk.Label(scenario_card, text="SUMO Scenario (.sumocfg)", style="Header.TLabel").grid(row=0, column=0, sticky="w", padx=4, pady=2)
        cfg_entry = tk.Entry(scenario_card, textvariable=self.sumo_cfg_var, font=("Segoe UI", 9), bg="#18181b", fg="#fafafa", insertbackground="white", relief="flat")
        cfg_entry.grid(row=1, column=0, sticky="ew", padx=4, pady=4)
        scenario_card.columnconfigure(0, weight=1)

        browse_btn = ttk.Button(scenario_card, text="Browse...", style="Secondary.TButton", command=self._browse_scenario)
        browse_btn.grid(row=1, column=1, padx=6)

        # 3. Parameters Grid Card
        param_card = ttk.Frame(main_container, style="Card.TFrame", padding=10)
        param_card.pack(fill="x", pady=(0, 10))
        ttk.Label(param_card, text="Simulation & Experiment Parameters", style="Header.TLabel").grid(row=0, column=0, columnspan=4, sticky="w", pady=(0, 6))

        params = [
            ("Integration Start (s):", self.integration_start_var, 0, 0),
            ("Experiment Start (s):", self.experiment_start_var, 0, 2),
            ("Experiment End (s):", self.experiment_end_var, 1, 0),
            ("Step Length (s):", self.steplength_var, 1, 2),
            ("Lateral Resolution (m):", self.lateral_res_var, 2, 0),
            ("Subscription Radius (m):", self.radius_var, 2, 2),
            ("Zoom (SUMO-GUI):", self.zoom_var, 3, 0),
        ]

        for label_text, var, r, c in params:
            ttk.Label(param_card, text=label_text, style="CardLabel.TLabel").grid(row=r + 1, column=c, sticky="w", padx=6, pady=4)
            ent = tk.Entry(param_card, textvariable=var, width=14, font=("Segoe UI", 9), bg="#18181b", fg="#fafafa", insertbackground="white", relief="flat")
            ent.grid(row=r + 1, column=c + 1, sticky="w", padx=6, pady=4)

        # Options Checkboxes
        check_frame = ttk.Frame(param_card, style="Card.TFrame")
        check_frame.grid(row=5, column=0, columnspan=4, sticky="w", pady=(8, 0))

        ttk.Checkbutton(check_frame, text="Run SUMO with GUI", variable=self.use_gui_var, style="TCheckbutton").pack(side="left", padx=(6, 18))
        ttk.Checkbutton(check_frame, text="Calculate RTF", variable=self.calc_rtf_var, style="TCheckbutton").pack(side="left", padx=18)
        ttk.Checkbutton(check_frame, text="Free camera (no follow ego)", variable=self.free_cam_var, style="TCheckbutton").pack(side="left", padx=18)

        # 4. Live Telemetry Dashboard Card
        telemetry_card = ttk.Frame(main_container, style="Card.TFrame", padding=10)
        telemetry_card.pack(fill="x", pady=(0, 10))

        ttk.Label(telemetry_card, text="Live Co-Simulation Telemetry", style="Header.TLabel").grid(row=0, column=0, columnspan=4, sticky="w", pady=(0, 6))

        metrics = [
            ("SIMULATION TIME", self.stat_sim_time),
            ("ACTIVE VEHICLES", self.stat_vehicles),
            ("REAL-TIME FACTOR", self.stat_rtf),
            ("EGO SPEED", self.stat_speed),
        ]

        for i, (title, var) in enumerate(metrics):
            sub_box = ttk.Frame(telemetry_card, style="Card.TFrame", padding=6)
            sub_box.grid(row=1, column=i, sticky="nsew", padx=4)
            telemetry_card.columnconfigure(i, weight=1)
            ttk.Label(sub_box, text=title, font=("Segoe UI", 8), foreground="#a1a1aa", background="#27272a").pack(anchor="w")
            ttk.Label(sub_box, textvariable=var, style="CardVal.TLabel").pack(anchor="w")

        status_bar = ttk.Frame(telemetry_card, style="Card.TFrame", padding=(4, 6))
        status_bar.grid(row=2, column=0, columnspan=4, sticky="ew", pady=(8, 0))
        self.lbl_status = ttk.Label(status_bar, textvariable=self.stat_unity_status, font=("Segoe UI", 9, "bold"), foreground="#eab308", background="#27272a")
        self.lbl_status.pack(side="left")
        ttk.Label(status_bar, text="Status: ", font=("Segoe UI", 9), foreground="#71717a", background="#27272a").pack(side="left")
        ttk.Label(status_bar, textvariable=self.stat_recording, font=("Segoe UI", 9, "bold"), foreground="#38bdf8", background="#27272a").pack(side="right")

        # 5. Execution Buttons & Footer
        btn_frame = ttk.Frame(main_container)
        btn_frame.pack(fill="x", pady=6)

        self.btn_start = ttk.Button(btn_frame, text="▶ Start Simulation", style="Start.TButton", command=self.start_simulation)
        self.btn_start.pack(side="left", padx=4)

        self.btn_stop = ttk.Button(btn_frame, text="■ Stop Simulation", style="Stop.TButton", command=self.stop_simulation, state="disabled")
        self.btn_stop.pack(side="left", padx=4)

        ttk.Button(btn_frame, text="Publications", style="Secondary.TButton", command=self._show_publications).pack(side="right", padx=4)
        ttk.Button(btn_frame, text="Contact / Info", style="Secondary.TButton", command=self._show_contact).pack(side="right", padx=4)

    def _start_banner_slideshow(self) -> None:
        if len(self.banner_images) > 1:
            self.current_banner_idx = (self.current_banner_idx + 1) % len(self.banner_images)
            self.banner_label.configure(image=self.banner_images[self.current_banner_idx])
            self.banner_timer_id = self.root.after(3500, self._start_banner_slideshow)

    def _auto_detect_scenario(self) -> None:
        scenarios = find_available_scenarios(self.project_root)
        if scenarios:
            self.sumo_cfg_var.set(scenarios[0])

    def _browse_scenario(self) -> None:
        chosen = filedialog.askopenfilename(
            title="Select SUMO Configuration File",
            initialdir=self.project_root,
            filetypes=[("SUMO Config", "*.sumocfg"), ("All Files", "*.*")],
        )
        if chosen:
            self.sumo_cfg_var.set(chosen)

    def _telemetry_callback(self, data: dict) -> None:
        """Called from SyncEngine thread on each step; posts update to Tkinter main thread."""
        def update():
            self.stat_sim_time.set(f"{data.get('sim_time', 0.0):.1f} s")
            in_range = data.get('active_vehicles', 0)
            total = data.get('total_sumo_vehicles', 0)
            self.stat_vehicles.set(f"{in_range} / {total}")
            self.stat_rtf.set(f"{data.get('rtf', 1.0):.2f}x")
            spd_ms = data.get('ego_speed', 0.0)
            self.stat_speed.set(f"{(spd_ms * 3.6):.1f} km/h")

            if data.get("unity_connected"):
                self.lbl_status.configure(foreground="#22c55e")
                self.stat_unity_status.set("● Unity Connected")
            else:
                self.lbl_status.configure(foreground="#eab308")
                self.stat_unity_status.set("○ Waiting for Unity...")

            if data.get("recording"):
                self.stat_recording.set("● RECORDING ACTIVE")
            else:
                self.stat_recording.set("ARMED (Waiting for start time)")

        self.root.after(0, update)

    def start_simulation(self) -> None:
        try:
            config = SimulationConfig(
                sumo_cfg=self.sumo_cfg_var.get().strip(),
                integration_start_time=float(self.integration_start_var.get()),
                experiment_start_time=float(self.experiment_start_var.get()),
                experiment_end_time=float(self.experiment_end_var.get()),
                step_length=float(self.steplength_var.get()),
                lateral_resolution=float(self.lateral_res_var.get()),
                zoom=float(self.zoom_var.get()),
                subscribe_radius=float(self.radius_var.get()),
                use_gui=self.use_gui_var.get(),
                calc_rtf=self.calc_rtf_var.get(),
                free_cam=self.free_cam_var.get(),
                results_dir=self.results_dir,
            )
        except ValueError:
            messagebox.showerror("Invalid Input", "Please enter valid numeric values for simulation parameters.")
            return

        errors = config.validate()
        if errors:
            messagebox.showerror("Configuration Error", "\n".join(errors))
            return

        self.btn_start.configure(state="disabled")
        self.btn_stop.configure(state="normal")
        self.stat_unity_status.set("Starting SUMO...")

        self.engine = SyncEngine(config=config, on_step_callback=self._telemetry_callback)

        def runner():
            try:
                self.engine.start()
            except Exception as e:
                self.root.after(0, lambda: messagebox.showerror("Simulation Error", str(e)))
            finally:
                self.root.after(0, self._on_sim_terminated)

        self.sim_thread = threading.Thread(target=runner, name="Sumo2Unity-Worker", daemon=True)
        self.sim_thread.start()

    def stop_simulation(self) -> None:
        if self.engine:
            self.engine.stop()
        self._on_sim_terminated()

    def _on_sim_terminated(self) -> None:
        self.btn_start.configure(state="normal")
        self.btn_stop.configure(state="disabled")
        self.stat_unity_status.set("Stopped")
        self.stat_recording.set("IDLE")

    def _show_publications(self) -> None:
        pub_text = (
            "1. Mohammadi, A., Park, P. Y., Nourinejad, M., Cherakkatil, M. S. B., & Park, H. S. (2024, June).\n"
            "   SUMO2Unity: An Open-Source Traffic Co-Simulation Tool to Improve Road Safety.\n"
            "   In 2024 IEEE Intelligent Vehicles Symposium (IV) (pp. 2523-2528). IEEE.\n\n"
            "2. Mohammadi, A., Cherakkatil, M. S. B., Park, P. Y., Nourinejad, M., & Asgary, A. (2025).\n"
            "   An Open-Source Virtual Reality Traffic Co-Simulation for Enhanced Traffic Safety Assessment.\n"
            "   Applied Sciences, 15(17), 9351."
        )
        messagebox.showinfo("Research Publications", pub_text)

    def _show_contact(self) -> None:
        info_text = (
            f"{VERSION}\n\n"
            f"Author: {AUTHOR}\n"
            f"{AFFILIATION}\n"
            "Recreated & Enhanced Modular Python Backend\n"
            f"Portfolio: {PORTFOLIO_URL}\n\n"
            "Built on the open-source SUMO2Unity project by\n"
            "Ahmad Mohammadi, PhD - York University\n"
            "License: MIT"
        )
        messagebox.showinfo("About & Contact", info_text)


def launch_gui() -> None:
    root = tk.Tk()
    app = Sumo2UnityApp(root)
    root.mainloop()
