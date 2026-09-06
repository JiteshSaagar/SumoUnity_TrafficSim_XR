"""
ZeroMQ Bridge for high-throughput communication with Unity.
- PUB socket on tcp://*:5556 (broadcasts vehicle, light states & commands)
- ROUTER socket on tcp://*:5557 (receives ego vehicle telemetry from Unity Dealer socket)
"""

import logging
import queue
import threading
import time
from typing import Any, Dict, List, Optional
import zmq

from .serializers import (
    build_command_message,
    build_persons_message,
    build_traffic_lights_message,
    build_vehicles_message,
    parse_unity_message,
)

logger = logging.getLogger("sumo2unity.zmq")


class ZMQBridge:
    def __init__(self, pub_port: int = 5556, router_port: int = 5557):
        self.pub_port = pub_port
        self.router_port = router_port
        self.context: Optional[zmq.Context] = None
        self.pub_socket: Optional[zmq.Socket] = None
        self.router_socket: Optional[zmq.Socket] = None

        self.rx_queue: queue.Queue = queue.Queue(maxsize=1000)
        self.is_running = False
        self._rx_thread: Optional[threading.Thread] = None

        self.last_rx_time = 0.0
        self.messages_received = 0
        self.messages_sent = 0

    def start(self) -> None:
        """Initializes sockets and starts receiver thread."""
        logger.info("Initializing ZeroMQ sockets (PUB: %d, ROUTER: %d)...", self.pub_port, self.router_port)
        self.context = zmq.Context()

        # PUB Socket
        self.pub_socket = self.context.socket(zmq.PUB)
        self.pub_socket.setsockopt(zmq.SNDHWM, 2000)
        self.pub_socket.setsockopt(zmq.LINGER, 0)
        self.pub_socket.bind(f"tcp://*:{self.pub_port}")

        # ROUTER Socket (pairs with Unity's DEALER socket)
        self.router_socket = self.context.socket(zmq.ROUTER)
        self.router_socket.setsockopt(zmq.RCVHWM, 2000)
        self.router_socket.setsockopt(zmq.LINGER, 0)
        self.router_socket.bind(f"tcp://*:{self.router_port}")

        self.is_running = True
        self._rx_thread = threading.Thread(target=self._rx_loop, name="ZMQ-Unity-Receiver", daemon=True)
        self._rx_thread.start()
        logger.info("ZeroMQ Bridge online and listening.")

    def _rx_loop(self) -> None:
        """Background thread receiving messages from Unity Dealer socket."""
        poller = zmq.Poller()
        if self.router_socket:
            poller.register(self.router_socket, zmq.POLLIN)

        while self.is_running:
            try:
                socks = dict(poller.poll(timeout=100))
                if self.router_socket in socks and socks[self.router_socket] == zmq.POLLIN:
                    frames = self.router_socket.recv_multipart(flags=zmq.NOBLOCK)
                    # DEALER sends [message] or [empty, message], ROUTER prepends [identity]
                    if len(frames) >= 2:
                        raw_payload = frames[-1]
                        parsed = parse_unity_message(raw_payload)
                        if parsed:
                            self.last_rx_time = time.time()
                            self.messages_received += 1
                            # Discard oldest if queue full to keep freshest state
                            if self.rx_queue.full():
                                try:
                                    self.rx_queue.get_nowait()
                                except queue.Empty:
                                    pass
                            self.rx_queue.put_nowait(parsed)
            except zmq.ZMQError as err:
                if self.is_running:
                    logger.debug("ZMQ receive error: %s", err)
            except Exception as ex:
                if self.is_running:
                    logger.error("Unexpected error in ZMQ receiver: %s", ex)
                time.sleep(0.01)

    def get_latest_unity_data(self) -> Optional[List[Dict[str, Any]]]:
        """Pulls the most recent vehicle telemetry from Unity."""
        latest = None
        while not self.rx_queue.empty():
            try:
                latest = self.rx_queue.get_nowait()
            except queue.Empty:
                break
        return latest

    def send_vehicles(self, vehicles: List[Dict[str, Any]]) -> None:
        """Publishes vehicle list to Unity."""
        if not self.pub_socket or not self.is_running:
            return
        payload = build_vehicles_message(vehicles)
        self.pub_socket.send_string(payload)
        self.messages_sent += 1

    def send_persons(self, persons: List[Dict[str, Any]]) -> None:
        """Publishes pedestrian list to Unity."""
        if not self.pub_socket or not self.is_running:
            return
        payload = build_persons_message(persons)
        self.pub_socket.send_string(payload)
        self.messages_sent += 1

    def send_traffic_lights(self, lights: List[Dict[str, Any]]) -> None:
        """Publishes traffic light states to Unity."""
        if not self.pub_socket or not self.is_running:
            return
        payload = build_traffic_lights_message(lights)
        self.pub_socket.send_string(payload)
        self.messages_sent += 1

    def send_command(self, command: str) -> None:
        """Publishes a control command (e.g. START_RECORDING, STOP_RECORDING)."""
        if not self.pub_socket or not self.is_running:
            return
        payload = build_command_message(command)
        self.pub_socket.send_string(payload)
        self.messages_sent += 1
        logger.info("Sent command to Unity: %s", command)

    def is_unity_connected(self, timeout_sec: float = 2.0) -> bool:
        """Returns True if messages were received within timeout."""
        return (time.time() - self.last_rx_time) < timeout_sec if self.last_rx_time > 0 else False

    def close(self) -> None:
        """Gracefully closes all sockets and terminates receiver thread."""
        logger.info("Closing ZeroMQ Bridge...")
        self.is_running = False
        if self._rx_thread and self._rx_thread.is_alive():
            self._rx_thread.join(timeout=0.5)

        for sock in (self.pub_socket, self.router_socket):
            if sock:
                try:
                    sock.close(linger=0)
                except Exception:
                    pass

        if self.context:
            try:
                self.context.term()
            except Exception:
                pass

        logger.info("ZeroMQ Bridge closed.")
