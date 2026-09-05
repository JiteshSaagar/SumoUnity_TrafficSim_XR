"""
Analytics and performance reporting (RTF calculation, report generation).
Compatible with rtf2chart.py.
"""

import logging
import math
import os
import time
from typing import List, Optional

logger = logging.getLogger("sumo2unity.analytics")


class MetricsLogger:
    def __init__(self, results_dir: str):
        self.results_dir = os.path.abspath(results_dir)
        os.makedirs(self.results_dir, exist_ok=True)
        self.rtf_file_path = os.path.join(self.results_dir, "rtf_report.txt")
        self.rtf_file = None

        self.last_sim_time: Optional[float] = None
        self.last_wall_time: Optional[float] = None
        self.start_sim_time: Optional[float] = None
        self.start_wall_time: Optional[float] = None

        self.rtf_history: List[float] = []
        self.current_rtf: float = 1.0

    def start(self) -> None:
        """Initializes the rtf_report.txt file with required header."""
        try:
            self.rtf_file = open(self.rtf_file_path, "w", encoding="utf-8")
            self.rtf_file.write("Time(s);RTF\n")
            self.rtf_file.flush()
            logger.info("RTF report initialized at %s", self.rtf_file_path)
        except Exception as err:
            logger.error("Failed to initialize RTF report: %s", err)

    def record_step(self, current_sim_time: float) -> float:
        """Computes Real-Time Factor (RTF) for current step and logs periodically."""
        now = time.perf_counter()

        if self.last_sim_time is None:
            self.start_sim_time = current_sim_time
            self.start_wall_time = now
            self.last_sim_time = current_sim_time
            self.last_wall_time = now
            return 1.0

        sim_delta = current_sim_time - self.last_sim_time
        wall_delta = now - self.last_wall_time

        if wall_delta > 0:
            step_rtf = sim_delta / wall_delta
            self.current_rtf = step_rtf
            self.rtf_history.append(step_rtf)

            if self.rtf_file:
                # Log relative simulation time and RTF
                rel_time = current_sim_time - (self.start_sim_time or 0.0)
                self.rtf_file.write(f"{rel_time:.2f};{step_rtf:.2f}\n")
                self.rtf_file.flush()

        self.last_sim_time = current_sim_time
        self.last_wall_time = now
        return self.current_rtf

    def get_overall_rtf(self) -> float:
        """Returns the mean RTF over the entire session."""
        if not self.rtf_history:
            return 0.0
        return sum(self.rtf_history) / len(self.rtf_history)

    def close(self) -> None:
        """Closes file handle."""
        if self.rtf_file:
            try:
                self.rtf_file.flush()
                self.rtf_file.close()
            except Exception:
                pass
            self.rtf_file = None
        overall = self.get_overall_rtf()
        logger.info("RTF logging finalized. Overall Average RTF: %.2f", overall)
