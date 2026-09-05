# Sumo2Unity Project Status & Technical Documentation

## 1. Project Overview
**Sumo2Unity** is an open-source co-simulation platform bridging **Eclipse SUMO** (Simulation of Urban MObility) with the **Unity 3D Game Engine / XR (VR) Platform**. It enables researchers and developers to run realistic microscopic traffic simulations and visualize them with high-fidelity 3D assets, vehicles, pedestrians, traffic lights, and human-in-the-loop VR driver/cyclist/pedestrian experiments.

---

## 2. System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       Eclipse SUMO                          │
│     (sumo-gui / sumo with .sumocfg, .net.xml, .rou.xml)     │
└───────────────────────────┬─────────────────────────────────┘
                            │ TraCI API (TCP / Localhost)
┌───────────────────────────▼─────────────────────────────────┐
│              Sumo2Unity Backend (Python)                    │
│   (RequiredFiles/Sumo2UnityTool_combined.py & sumo2unity)   │
│                                                             │
│  • High-precision time sync (0.10s / 10 Hz)                 │
│  • ZeroMQ PUB Socket    (tcp://*:5556) -> To Unity          │
│  • ZeroMQ ROUTER Socket (tcp://*:5557) <- From Unity        │
│  • Real-Time Factor (RTF) performance monitor               │
│  • Modern GUI Dashboard / Headless CLI mode                 │
└─────────────┬─────────────────────────────────▲─────────────┘
              │ PUB (Port 5556)                 │ ROUTER (Port 5557)
              ▼                                 │
┌───────────────────────────────────────────────┴─────────────┐
│                       Unity Engine                          │
│                                                             │
│  • ExchangeData.cs       : NetMQ Sub/Dealer communication   │
│  • SimulationController.cs: Vehicle spawner & signal sync    │
│  • RoadNetworkBuilder.cs : 3D procedural mesh generator     │
│  • VehicleController.cs  : Physics, rotation & lerp         │
│  • EyeDataLogger.cs      : VR Eye tracking recorder         │
│  • Fps.cs                : Real-time framerate logger       │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Step-by-Step Guide: Running Scenario 1 in Unity

### Step 1: Open the Unity Project & Scene
1. Open **Unity Hub**.
2. Open this project with Unity (recommended: **Unity 6 / 6000.0.x**).
3. In the Unity Project window, navigate to:
   `Assets/Scenes/Scenario1.unity` (or your target scenario scene) and double-click to open it.

### Step 2: (Optional) Build or Rebuild the 3D Road Network
If the road network is not already built in the scene or if you modified `Sumo2Unity.net.xml`:
1. In the top Unity menu, click **Sumo2Unity -> 1. Create Road Network**.
2. In the editor window, click **Select Sumo Files Folder** and choose:
   `.../SumoUnity_TrafficSim_XR/Scenario1`
3. Click **Start**.
   - `RoadNetworkBuilder` will parse `Sumo2Unity.net.xml` and `Sumo2Unity.poly.xml`.
   - It procedurally generates 3D road meshes, junctions, lane markings, zebra crossings, and terrain polygons under a `RoadNetworkRoot` GameObject.

### Step 3: Start the Python Backend
Open a terminal in the project root and run the Python backend:

#### Option A: Using the Modern GUI Dashboard (Recommended)
```powershell
python RequiredFiles/Sumo2UnityTool_combined.py
```
1. The **Sumo2Unity Dashboard** will open.
2. Under **SUMO Scenario (.sumocfg)**, confirm it points to:
   `Scenario1/Sumo2Unity.sumocfg` (it auto-detects it).
3. Verify the default parameters:
   - **Integration Start (s)**: `540.0` (or `0.0` if you want instant vehicle streaming)
   - **Experiment Start (s)**: `600.0`
   - **Experiment End (s)**: `720.0`
   - **Step Length (s)**: `0.10`
   - **Run SUMO with GUI**: Checked
4. Click **▶ Start Simulation**.
   - Eclipse SUMO (or `sumo-gui`) will launch and start stepping.
   - The dashboard will show live metrics (Simulation Time, Active Vehicles, RTF, Ego Speed).

#### Option B: Using the Headless CLI
```powershell
python RequiredFiles/Sumo2UnityTool_combined.py --config Scenario1/Sumo2Unity.sumocfg --integration-start 0.0 --experiment-end 3600.0
```

### Step 4: Press Play in Unity
1. Return to the Unity Editor.
2. In the scene hierarchy, inspect the GameObject having **`SimulationController`**:
   - Ensure `egoVehicle` is assigned (e.g. `EgoCar`, `EgoBike`, or `EgoScooter`).
   - Ensure `egoVehicleId` matches the ego trip in `Sumo2Unity.rou.xml` (default: `f_0.0`).
   - Ensure `carModelsList` has your 3D vehicle prefabs assigned (e.g. `EloraGold` for type `301`, etc.).
3. Click the **Play (▶)** button in the Unity toolbar.
4. **Data streaming begins**:
   - Unity connects to ZeroMQ on `tcp://localhost:5556` (PUB) and `tcp://localhost:5557` (ROUTER).
   - SUMO AI vehicles spawn into the 3D scene matching their SUMO coordinates and headings.
   - When the user drives the Ego vehicle (keyboard or VR headset/controllers), its coordinates are sent back to SUMO, causing SUMO AI traffic to react and yield to the user in real time.
   - Traffic lights dynamically switch between Red, Yellow, and Green matching the SUMO signal phases.

### Step 5: Stop the Session & Analyze Results
1. Click **■ Stop Simulation** in the Python GUI (or press Stop in Unity).
2. The co-simulation session will conclude, generating reports in the `Results/` directory:
   - `Results/rtf_report.txt`: Real-Time Factor log over time.
   - `Results/FPS_Report.txt`: Unity client FPS recorded every 0.1s.
   - `Results/vehicle_data_report.txt`: Trajectory timestamps `(x, y, z)` for all vehicles.
   - `Results/EyeTrackingLog.csv`: VR HMD eye gaze tracking data.
3. You can plot the performance graphs using the included scripts:
   ```powershell
   python Results/rtf2chart/rtf2chart.py
   python Results/fps2chart/fps2chart.py
   ```

---

## 4. Communication Protocols & Ports

| Port | Protocol | Sender | Receiver | Description |
|---|---|---|---|---|
| **5556** | ZeroMQ PUB/SUB | Python Backend | Unity (`ExchangeData.cs`) | Broadcasts vehicle positions, traffic light states, and recording commands. |
| **5557** | ZeroMQ ROUTER/DEALER | Unity (`ExchangeData.cs`) | Python Backend | Sends Ego vehicle position, heading, velocity, and actor type. |

### Message Formats (JSON):
- **Vehicles Message (Python -> Unity)**:
  ```json
  {
    "type": "vehicles",
    "vehicles": [
      {
        "vehicle_id": "f_1.0",
        "position": [120.45, 85.32, 0.0],
        "angle": 90.0,
        "type": "301",
        "long_speed": 13.88,
        "vert_speed": 0.0,
        "lat_speed": 0.0
      }
    ]
  }
  ```
- **Traffic Lights Message (Python -> Unity)**:
  ```json
  {
    "type": "trafficlights",
    "lights": [
      {
        "junction_id": "J1",
        "state": "GGrr"
      }
    ]
  }
  ```
- **Control Commands (Python -> Unity)**:
  ```json
  {
    "type": "command",
    "command": "START_RECORDING"
  }
  ```
- **Ego Vehicle Telemetry (Unity -> Python)**:
  ```json
  {
    "vehicles": [
      {
        "vehicle_id": "f_0.0",
        "position": [10.2, 5.4, 0.0],
        "angle": 90.0,
        "type": "ego",
        "long_speed": 12.5,
        "vert_speed": 0.0,
        "lat_speed": 0.0
      }
    ]
  }
  ```

---

## 5. Current Implementation Status

| Feature / Subsystem | Status | Details |
|---|---|---|
| **SUMO TraCI Core** | ✅ Complete | Robust `SumoManager` supporting `sumo` & `sumo-gui`, auto `SUMO_HOME` detection. |
| **ZeroMQ Async Bridge** | ✅ Complete | High-throughput non-blocking PUB (5556) and ROUTER (5557) threads. |
| **Time & Step Pacing** | ✅ Complete | Sub-millisecond precision sleep with `perf_counter` and RTF tracking. |
| **Multi-Actor Injection** | ✅ Complete | Supports cars, dynamic bikes, scooters, and pedestrians (`moveToXY`). |
| **Modern GUI Dashboard** | ✅ Complete | Dark-themed dashboard with live telemetry cards (RTF, vehicles, ego speed). |
| **CLI / Headless Mode** | ✅ Complete | Full `--headless` mode for automated testing and CI pipelines. |
| **Unity Road Builder** | ✅ Complete | Reads XML and builds 3D roads, crossings, and terrain in editor mode. |
| **XR & VR Integration** | ✅ Complete | XR Origin rig, steering/pedal controls, eye-tracking logger. |
| **Experiment Analytics** | ✅ Complete | Unified reporting to `Results/` compatible with `rtf2chart` and `fps2chart`. |
