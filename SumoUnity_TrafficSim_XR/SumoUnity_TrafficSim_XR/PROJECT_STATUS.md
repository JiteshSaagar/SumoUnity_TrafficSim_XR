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
│  • LaneMarkingMesh.cs    : Lane paint carrier ribbon mesh   │
│  • RoadMarking.shader    : Analytic paint coverage + dashes │
│  • GrassField.cs         : Camera-local procedural grass    │
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

### Step 2: Build or Rebuild the 3D Road Network
> **A rebuild is REQUIRED, and you must SAVE the scene afterwards (Ctrl+S).**
> A rebuild only changes objects in memory; without the save, the scene on disk
> keeps its previous markings and nothing appears to change. See §6.

1. In the top Unity menu, click **Sumo2Unity -> 1. Create Road Network**.
2. In the editor window, click **Select Sumo Files Folder** and choose:
   `.../SumoUnity_TrafficSim_XR/Scenario1`
3. Click **Start**.
   - `RoadNetworkBuilder` will parse `Sumo2Unity.net.xml` and `Sumo2Unity.poly.xml`.
   - It procedurally generates 3D road meshes, junctions, lane markings, zebra crossings, and terrain polygons under a `RoadNetworkRoot` GameObject.

**No Inspector wiring is needed for lane markings.** The builder auto-loads
`Assets/_Project/Materials/Mat_LaneMarking.mat`. Leave the `Lane Marking
Material` slot on `RoadNetworkBuilder` empty unless you deliberately want a
different paint material — and if you do assign one, it must be an **opaque
surface material, never a URP decal material** (see §6).

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
| **Lane Markings** | ✅ Complete | Carrier ribbon meshes with analytic per-pixel paint coverage; no sub-pixel dropout, no Z-fighting. Replaced per-dash URP DecalProjectors. See §6. |
| **XR & VR Integration** | ✅ Complete | XR Origin rig, steering/pedal controls, eye-tracking logger. |
| **Ground & Grass** | ✅ Complete | World-space ground UVs (constant texel density) + procedural grass generated around the camera. See §7. |
| **Experiment Analytics** | ✅ Complete | Unified reporting to `Results/` compatible with `rtf2chart` and `fps2chart`. |

---

## 6. Lane Marking System

### 6.1 Why it changed
The original implementation spawned **one `DecalProjector` GameObject every 3 m**
of every lane boundary — ~536 objects in Scenario1, ~2241 in Scenario2. Each was
a separate screen-space projection pass, and they Z-fought the road in stereo.

Lane paint is now **one child mesh per lane boundary**, two per lane.

| | Scenario1 | Scenario2 |
|---|---|---|
| Old `DecalProjector` objects | ~536 | ~2241 |
| New `MeshRenderer` objects | 46 | 88 |

Markings remain children of their `LaneSegment_*` object, and the road keeps its
own material — the asphalt is untouched, so lanes and junctions still match.

### 6.2 The carrier ribbon: why the mesh is wider than the paint
A lane line drawn as geometry exactly its own width cannot work. A 0.12 m line
lying flat and viewed **across** the road is foreshortened hard by the shallow
elevation angle; at 1920 px and ~1.2 m eye height it projects to 0.34 px at 20 m
and 0.15 px at 30 m. The rasterizer catches some pixel centres and misses
others, so a solid line reads as dashes that crawl as the camera moves. Looking
**along** the road the line runs toward the horizon and covers many pixels
lengthwise, so it stays solid — exactly the angle dependence seen in testing.

MSAA does not rescue this (PC is already at 4x): a 0.15 px feature misses all
four coverage samples most frames.

> **Why not extrude the paint into a thin 3D box?** It does not help. The top
> face is still 0.15 px at 30 m and drops out identically. A side face tall
> enough to be visible (~1 cm) is a kerb, not paint: it catches specular
> highlights, casts ridge shadows, can interfere with wheel raycasts, and
> doubles the vertex count. Real thermoplastic paint is 0.5-3 mm, about 0.1 px
> at 30 m. The rule is that **geometry cannot be prefiltered, but analytic
> coverage can** — which is what mipmaps do for textures.

So the mesh is **not** the paint. It is an oversized *carrier* ribbon,
`markingWidth + 2 x 0.15 m` across, and the line is drawn inside it in two
stages:

1. **Vertex** — the carrier is widened about its centre line until it covers at
   least `_MinPixelWidth` pixels, so the rasterizer can never miss it. The
   interpolator tracks the widening, so the fragment stage still sees each
   pixel's *true* world distance from the centre line and the painted line does
   not grow with the carrier.
2. **Fragment** — coverage is the exact overlap between the painted band and
   this pixel's footprint:

```hlsl
float halfFootprint = max(fwidth(d) * 0.5 * _EdgeSoftness, 1e-6);
float lo = max(d - halfFootprint, -hw);
float hi = min(d + halfFootprint,  hw);
return saturate((hi - lo) / (2.0 * halfFootprint));
```

`fwidth(d)` is how much the lane coordinate changes across one pixel:
millimetres up close, metres at distance. Coverage is therefore **computed, not
sampled**, so a line a tenth of a pixel wide contributes a tenth of a pixel of
white instead of flickering.

> **This must be a box filter, not a `smoothstep`.** A smoothstep saturates near
> 0.5 at the band centre however thin the line gets — at a 2 m footprint it
> reports 0.545 where the correct value is 0.060, which would make distant roads
> glow. The overlap integral above is exact at every scale.

Verified against the true area average, and against what plain geometry does
(sampling a pixel on the line while the camera creeps forward in sub-pixel
steps):

| Distance | Carrier on screen | Coverage | True average | Point-sampled flicker |
|---|---|---|---|---|
| 10 m | 4.7 px | 1.000 | 1.000 | 0.000 |
| 20 m | 1.2 px → widened | 0.336 | 0.336 | **1.000** |
| 30 m | 0.5 px → widened | 0.150 | 0.150 | **1.000** |
| 50 m | 0.2 px → widened | 0.054 | 0.054 | **1.000** |
| 75 m | 0.1 px → widened | 0.024 | 0.024 | **1.000** |

Coverage matches the true average exactly at every distance, while plain
geometry swings fully on and off — that swing is the crawling-dash artifact.

Because the paint fades rather than dropping out, it has to blend: the material
is in the **transparent** queue (`Transparent-100`) with `ZWrite Off`. The
ShaderLab `Offset` still applies to the depth *test*, which is what keeps
coplanar paint from Z-fighting the asphalt.

**Tuning on `Mat_LaneMarking`:**
- `Marking Width (m)` — the painted width you actually see. Keep it within the
  carrier the mesh was built with, i.e. builder width + 0.30 m, or it gets clipped.
- `Min Carrier Width (px)` — raise if distant lines still break up.
- `Max Widening Factor` — caps how large a distant carrier may grow.
- `Edge Softness` — 1.0 is mathematically correct; raise only if lines buzz.

### 6.3 Solid vs. broken lines
Dashes are drawn by the shader as a filtered function of distance along the
lane. The ribbon stays continuous — cutting real gaps into the geometry would
reintroduce the same aliasing along the road.

Select any `LaneSegment_*` and use `LaneMarkingController`:

| Field | Meaning |
|---|---|
| `Broken Left` | Dashes the **left** lane line (object named `LaneMarking_Right`) |
| `Broken Right` | Dashes the **right** lane line (object named `LaneMarking_Left`) |
| `Marking Width` | Width used to size the carrier at build time |

The left/right naming is deliberately swapped to match the original decal
naming, so the toggles keep the meaning they had before.

Toggling only rewrites that lane's vertex colours — no geometry rebuild, no
re-parsing of the SUMO network, and every marking still shares one material.
Dash length and gap are material properties (`Dash Length`, `Gap Length`).

### 6.4 Files
| File | Role |
|---|---|
| `Assets/_Project/Shaders/RoadMarking.shader` | Carrier widening + analytic paint coverage, full XR stereo macros |
| `Assets/_Project/Materials/Mat_LaneMarking.mat` | Paint material, auto-loaded by the builder |
| `Assets/_Project/Scripts/.../LaneMarkingMesh.cs` | Carrier ribbon geometry, mitered joints, junction clipping |
| `Assets/_Project/Scripts/.../LaneMarkingController.cs` | Per-lane solid/broken via vertex colours |

### 6.5 Important: material types are not interchangeable
`RoadMarking.mat` binds URP's package `Decal.shadergraph`. **A decal material
renders nothing through a normal `MeshRenderer`.** That is why lane lines use a
separate `laneMarkingMaterial` slot; the old `roadMarkingMaterial` field now
survives only as a fallback for zebra crossings.

### 6.6 Known notes
- **A rebuild only takes effect once the scene is saved.** Regenerating the road
  network changes objects in memory; without Ctrl+S the scene on disk keeps its
  previous markings and nothing appears to change. To confirm a rebuild landed,
  expand `LaneSegment_0`: it should have exactly **two** children,
  `LaneMarking_Left` and `LaneMarking_Right`, with no `_Decal` suffix.
- **Shared boundaries are painted twice.** Adjacent lanes each draw a line at
  the boundary they share. With blending this makes distant lines somewhat
  brighter than a single layer would be (two layers at 15% read as ~28%). Not a
  correctness problem, but it is why far-off lines can look a little strong.
  De-duplicating needs per-lane neighbour information, which is not implemented.
- **Beyond ~75 m the carrier hits `Max Widening Factor` and can drop out again.**
  At that range its coverage is under 2%, so the dropout is below the perceptual
  threshold. Raising the cap fixes it at the cost of very large distant quads.
- **Junction clipping is currently a no-op.** `ClipToOutside` stops paint at
  intersections, and a latent ordering bug was fixed along the way
  (`_junctionPolys2D` was populated *after* the lane pass, so the test always ran
  against an empty list). Verified against both networks this changes nothing
  visible: 100.0% of boundary length survives, because SUMO already ends normal
  lanes at the junction boundary and the builder skips the internal/crossing
  edges that would overlap.
- `Mobile_RPAsset` has MSAA off (`m_MSAA: 1`). 4x is close to free on tile-based
  mobile GPUs and standard for VR, though it is not what fixes the shimmer.

---

## 7. Ground: Texture Resolution and Grass

### 7.1 Ground texture resolution
`BuildPolygonGameObject` used to normalise polygon UVs to 0..1 across the whole
polygon, and `TerrainMaterial` then tiled that 15 x 20. Because the UV range was
fixed regardless of polygon size, one texture tile was stretched over:

| Scenario | Terrain bounds | Old tile size | New tile size |
|---|---|---|---|
| Scenario1 | 235 x 114 m | 15.7 x 5.7 m | 4 x 4 m |
| Scenario2 | 1302 x 639 m | **86.8 x 32.0 m** | 4 x 4 m |

That is why the ground read as blurry and smeared, badly so in Scenario2, and
why tiles were non-square (the aspect was distorted by the polygon's bounds).

Polygon UVs are now **world space in tile units**: `uv = worldXZ / polygonMetresPerTile`.
Texel density is then constant no matter how large the polygon is.

- `RoadNetworkBuilder.polygonMetresPerTile` (default **4 m**) controls ground
  sharpness. Lower it for a finer ground; raise it if the texture visibly repeats.
- **Ground materials must sit at tiling 1,1** now, because the UVs already carry
  the tiling. `TerrainMaterial` (was 15x20), `WoodMaterial` (was 200x200) and
  `ResidentialMaterial` (was 50x2) have been reset accordingly.

> If the ground still looks soft up close, that is the texture's own pixel count,
> not the mapping. Raise the import **Max Size** on `Assets/_Project/Textures/Grass.jpg`
> and set its filter mode to Trilinear with anisotropic level 4-8; aniso is what
> sharpens a ground plane viewed at a grazing angle.

### 7.2 Grass
Grass is generated over every polygon of type `Terrain`, and kept off roads,
junctions and crossings.

**It is generated around the camera at runtime, not baked.** Scenario2's terrain
is ~813,000 m²; at 10 blades/m² a static bake would be 8.1 million blades and
32.5 million triangles. Generating within `viewRadius` instead makes the cost
independent of map size:

| | Live | Static bake of Scenario2 |
|---|---|---|
| Blades | ~33,000 | 8,132,510 |
| Triangles | ~133,000 | 32,530,040 |
| Draw calls | 52 (one per cell) | — |

Cells are marked `HideAndDontSave`, so grass never enters the saved scene and
cannot bloat the scene file. Placement is hashed from cell coordinates, so a
cell regenerates identically when you drive back and the field does not
reshuffle.

**Blade height and occlusion.** You asked for grass tall enough to hide the
lanes beside you. From a 1.2 m eye height, with grass standing 5 m away:

| Road to hide at | Grass height needed |
|---|---|
| 10 m | 0.60 m |
| 20 m | 0.90 m |
| 30 m | 1.00 m |

The default is therefore **0.9 m**, which reads as a meadow rather than a mown
verge. Drop `Grass Height` to ~0.35 if you want a tidy verge and do not need the
occlusion.

**All grass parameters live in one place: the `Grass` block on the
`RoadNetworkBuilder` manager object.** Edits there apply to the generated
`GrassField` immediately — no road rebuild — and work in play mode as well as
edit mode, because the manager pushes its settings down on every inspector
change.

| Group | Parameters |
|---|---|
| — | `Enabled`, `Material` (auto-loads `Mat_Grass.mat` if empty) |
| Blades | `Density` (/m²), `Height` (m), `Height Variation`, `Width` (m) |
| Extent | `View Radius` (m), `Cell Size` (m), `Max Cells`, `Forward Bias`, `Bias Smoothing`, `Bias Speed Threshold` |
| Colour | `Base Color`, `Tip Color`, `Color Jitter` |
| Placement | `Road Margin` (m), `Root Sink` (m) |
| Wind and Shading | `Wind Strength`, `Wind Speed`, `Wind Scale`, `Backlight`, `Smoothness` |

The ones worth reaching for first:

| Parameter | Default | Effect |
|---|---|---|
| `Density` | 10 /m² | Cost scales linearly |
| `Height` | 0.9 m | See the occlusion table above |
| `View Radius` | 32 m | Cost scales with the **square** of this |
| `Forward Bias` | 0.5 | Pushes the circle ahead of the car. Free — see below |
| `Road Margin` | 0.25 m | How far back from kerbs grass stops |

**Forward bias.** Grass behind the car cannot be seen while driving, so centring
the circle on the camera spends half of it on wasted ground. `Forward Bias`
offsets the circle ahead by that fraction of `View Radius`. The circle keeps its
area, so this costs nothing at all:

| Forward Bias | Cells / draw calls | Triangles | Reach ahead | Reach behind |
|---|---|---|---|---|
| 0.0 (centred) | 52 | 133,120 | 32 m | 32 m |
| 0.3 | 52 | 133,120 | 40 m | 24 m |
| **0.5 (default)** | **52** | **133,120** | **48 m** | **16 m** |
| 0.7 | 52 | 133,120 | 56 m | 8 m |

At the default that is **50% more forward reach for zero extra cost**.

Two details make it usable in VR rather than nauseating:

- Above `Bias Speed Threshold` (1.5 m/s) the bias follows **travel direction,
  not gaze**. A head turn swings gaze far faster than the car can turn, and
  following it would drag the whole field sideways while driving.
- `Bias Smoothing` (0.6 s) damps the direction change. Without it, turning a
  corner would shift the field instantly and force every cell to regenerate in
  one frame.

Set `Forward Bias` to 0 to get the old camera-centred behaviour.

Blade size, colour and density are baked into the cell meshes, so changing them
regenerates the live cells. Wind and shading are pushed through a
`MaterialPropertyBlock` instead, so tuning them never edits `Mat_Grass` on disk
and never touches geometry.

The `GrassField` component itself shows the same values, plus a **Rebuild Grass
Now** button, a live cell count, and a **Select Manager Object** shortcut. It is
a mirror: the manager overwrites it whenever you edit there.

### 7.3 Known notes
- **Blades carry no texture.** Colour comes from vertex colours, dark at the base
  and lighter at the tip. This avoids the alpha-cutout overdraw that textured
  grass cards cost in VR, which matters far more here than blade detail does.
- Each blade is **two crossed quads**, not one. A single quad vanishes edge-on
  and the field would flicker as the driver turns.
- **Grass does not avoid buildings or trees.** Exclusion covers lanes, junctions
  and crossings, which are the polygons the builder knows about. Buildings are
  separately placed models, so blades can intersect them. Fixing that needs
  either colliders on the buildings and a raycast test, or their footprints fed
  into `GrassField.exclusionPolygons`.
- Grass casts no shadows: thousands of thin blades produce a shadow mess for no
  visual gain, and it is a large cost in VR.
- Grass needs a camera. In play mode it follows `Camera.main`, so the XR rig
  camera must be tagged **MainCamera**; in the editor it follows the Scene view.
- Grass cell GameObjects are `HideAndDontSave`, so they do not appear in the
  Hierarchy and are never saved. Only the `GrassField` object under
  `RoadNetworkRoot` is part of the scene.
- For Quest, start at `Grass Density` 6 and `Grass View Radius` 24 and measure
  before raising them.
