# Sumo2Unity Project Status & Technical Documentation

## 0. Authorship

**Jitesh Surendra Saagar** — Intern under **Dr. Anshuman Sharma, IIT BHU**
Portfolio: [jiteshsaagar.me](https://jiteshsaagar.me)

Recreated and enhanced the modular Python backend, the procedural road/lane
marking system, and the grass field.

Built on the open-source **SUMO2Unity** project by Ahmad Mohammadi, PhD —
York University. Licensed MIT; the upstream credit is retained in the GUI's
About dialog and source headers as that licence requires. The papers in the
GUI's *Research Publications* dialog are the upstream project's and are
unchanged.

---

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
- **Persons Message (Python -> Unity)**:
  ```json
  {
    "type": "persons",
    "persons": [
      {
        "person_id": "pf_0.3",
        "position": [-31.30, 9.00, 0.0],
        "angle": 0.0,
        "type": "DEFAULT_PEDTYPE",
        "speed": 1.34,
        "road_id": ":J8_c0",
        "state": "crossing"
      }
    ]
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
| **Multi-Actor Injection** | ✅ Complete | Cars, bikes, scooters, and — since Phase 2 — pedestrians, which are now created in SUMO rather than assumed to exist. See §8.10. |
| **Modern GUI Dashboard** | ✅ Complete | Dark-themed dashboard with live telemetry cards (RTF, vehicles, ego speed). Window/app title reads `SumoUnity_TrafficSim_XR v2.1` (from `VERSION` in `RequiredFiles/sumo2unity/gui/app.py`). |
| **CLI / Headless Mode** | ✅ Complete | Full `--headless` mode for automated testing and CI pipelines. |
| **Unity Road Builder** | ✅ Complete | Reads XML and builds 3D roads, crossings, and terrain in editor mode. |
| **Lane Markings** | ✅ Complete | Carrier ribbon meshes with analytic per-pixel paint coverage; no sub-pixel dropout, no Z-fighting. Replaced per-dash URP DecalProjectors. See §6. |
| **XR & VR Integration** | ✅ Complete | XR Origin rig, steering/pedal controls, eye-tracking logger. |
| **Ground & Grass** | ✅ Complete | World-space ground UVs (constant texel density) + procedural grass generated around the camera. See §7. |
| **SUMO → Unity Pedestrians** | ✅ Complete | Phase 1: `persons` channel end to end, verified over ZMQ. Needs a humanoid prefab or it draws capsules. See §8.9. |
| **XR Pedestrian Ego** | ✅ Complete | Phase 2: injected as a real SUMO person (`ped_xr`); vehicles yield to it at the zebra. See §8.10. |
| **Social Force Model** | ❌ Not implemented | Planned for Unity-side pedestrian agents. See §8.6. |
| **Pedestrian Network (SUMO)** | ✅ Complete | Phase 0: sidewalks on all 19 edges, 14 crossings, 20 walking areas, 85 peds / 300 s. See §8.8. |
| **Sidewalks / Walking Areas (3D)** | ⚠️ Partial | SUMO side done (§8.8); Unity still builds sidewalks as flat asphalt with no kerb and no walking areas. Phase 4. |
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
  **Measured 2026-09-06: `Scenario1` has zero cameras tagged MainCamera**, so
  `ResolveCamera()` returns null in play mode and grass does not generate at all
  while playing - it only appears in the Scene view. The desktop pedestrian rig
  (§8.11) tags its own camera MainCamera and fixes this as a side effect; the
  real XR rig needs the same tag.
- Grass cell GameObjects are `HideAndDontSave`, so they do not appear in the
  Hierarchy and are never saved. Only the `GrassField` object under
  `RoadNetworkRoot` is part of the scene.
- For Quest, start at `Grass Density` 6 and `Grass View Radius` 24 and measure
  before raising them.
- **Editing a default in `GrassSettings` does not change an existing scene.**
  Unity runs a field initialiser only when the component instance is first
  constructed; after that the values live in `Scenario1.unity` and are restored
  on every load, so the script defaults are never read again. Symptom: you edit
  `height = 0.4f` in code, press Play, and still get the old 0.9.
- **The manager's copy is authoritative, and it wins on every Play.**
  `RoadNetworkBuilder.OnValidate` pushes `grassSettings` down into the generated
  `GrassField` on every domain reload, which includes entering Play mode. So
  resetting the *field* alone does nothing lasting: the manager overwrites it the
  instant you press Play, and the old values are still there when you stop. Any
  fix has to change `RoadNetworkBuilder.grassSettings`.
  Fix: select **Managers**, and in the Road Network Builder inspector press
  **Reset Grass To Script Defaults**, then **save the scene**. The button keeps
  the material reference, which a plain component Reset would null - and a null
  material means no grass at all, with no error to explain it.
- **The generated `GrassField` is not a child of `Managers`.** It lives at
  `RoadNetworkRoot / GrassField`, a separate scene root, so `GetComponentInParent`
  from the field never finds the manager. Editor tooling must search the scene
  instead; `GrassFieldEditor.FindBuilderFor` does, and warns if it comes up empty.
- **Beware a stray `GrassField` component on the `Managers` object.** One exists
  in `Scenario1`. It has no terrain polygons so it renders nothing, but
  `GetComponentInChildren` *includes the GameObject it is called on*, so the
  manager could latch onto it and quietly send every grass edit into a dead
  component. `FindOwnedGrassField` now skips it, and the manager inspector offers
  a button to delete it.
- Day to day, tune grass in the **Inspector on the manager**, not in code - that
  is the authored source of truth.

---

## 8. Pedestrian Simulation & the Social Force Model

This section records what we took from Garrido et al. (2021), what we verified
ourselves against SUMO 1.26, where this project currently stands, and the plan
to get from here to a working pedestrian layer.

**Reference paper:** Daniel Garrido, João Jacob, Daniel Castro Silva, Rosaldo
J. F. Rossetti, *"Pedestrian Simulation in SUMO Through Externally Modelled
Agents"*, LIACC / FEUP, 2021.
Their codebase: `github.com/dalugoga/sumo-unity-distributed-pedestrian-simulator`.

### 8.1 Why SUMO's own pedestrian models are not enough

SUMO only gained pedestrians in 2014 — twelve years after its first release —
and it ships two models, neither built for safety research:

| Model | Behaviour | Verdict |
|---|---|---|
| `nonInteracting` | Constant speed, no interaction with anyone, teleports across intersections. | Useless for us. |
| `striping` (default) | Sidewalks and crossings are split into **lanes**, like a multi-lane road. A pedestrian shifts to an adjacent stripe to pass a slower one or avoid a head-on collision. Enables ped-vehicle interaction. | Efficient, but not human. |

The paper's central criticism of `striping`, and the reason we are building on
top of it rather than trusting it:

> *"In the real world, pedestrians don't move in lanes like cars do, don't
> always walk in their designated areas and might attempt to cross the road in
> places other than the crosswalk, or when the crosswalk signal indicates not
> to cross. These oversights limit the impact of pedestrian safety research
> made in SUMO."*

The fix the paper proposes — and the one we are adopting — is **not** to patch
SUMO's C++ source, but to model pedestrians **externally in Unity** and push
their positions back into SUMO over TraCI, so SUMO's vehicles still react to
them.

> **Note — SUMO 1.26 ships JuPedSim.** Our SUMO build reports the `JuPedSim`
> feature flag, so `--pedestrian.model jupedsim` is available and is a far
> better in-SUMO model than `striping`. It is worth benchmarking as a baseline,
> but it does **not** replace the plan below: JuPedSim cannot put a
> VR-headset-driven human into the crowd, which is the whole point here.

### 8.2 The paper's architecture — and how ours already compares

Garrido et al. use three modules, which map almost exactly onto what we already
have (§2):

| Paper's module | Our equivalent | Status |
|---|---|---|
| SUMO simulation (traffic + `striping` peds) | Same, `Scenario1/` | Done |
| Python middleware (TraCI to ZeroMQ) | `RequiredFiles/sumo2unity/` | Done |
| Unity3D (3D view + external ped models) | `Assets/_Project/Scripts/IntegrationScripts/` | Vehicles only |

Two places where **our implementation is already ahead of the paper's**:

- **Transport.** They run a strict request/response loop: Unity is the client,
  the middleware is the server, and each side blocks waiting for the other. Our
  bridge is asynchronous — PUB/SUB on 5556 outbound, ROUTER/DEALER on 5557
  inbound (§4) — so a slow Unity frame cannot stall the SUMO step.
- **Network build.** They state their Unity scene *"was created by hand that
  matched the original scene"* and explicitly call automation out of scope. Our
  `RoadNetworkBuilder` already generates the 3D network from `net.xml`
  automatically, including zebra crossings.

They also hit the Unity main-thread problem we already solved the same way: ZMQ
receive runs on a background thread and hands work to the main thread through a
queue (`SimulationController.mainThreadActions`, a `ConcurrentQueue<Action>`).

### 8.3 Verified against SUMO 1.26 — the paper's biggest workaround is obsolete

The paper's most painful limitation, and the source of its instability, was
`person.moveToXY`:

> *"while it can in fact move a pedestrian to any spot on the map, other
> pedestrians and vehicles will not become aware of its presence. This is due to
> the method not updating the pedestrian position in terms of edge, but only in
> terms of coordinates."*

Their workaround was to raycast down in Unity to detect the current edge, then
**delete and re-create the person** in SUMO whenever it changed edge — which
they report as *"prone to crashes"* and *"seemingly random crashes"* that got
worse with more pedestrians.

**We tested this directly against our own `Scenario1` on SUMO 1.26 and it no
longer applies.** Results:

| Test | Result |
|---|---|
| `person.moveToXY(..., keepRoute=2)` walked along sidewalk `E0`, then across to `-E0.30` | PASS — `getRoadID()` correctly followed `E0` to `-E0.30`, and `getLaneID()` to `-E0.30_0`. **The edge is remapped automatically.** |
| Positional fidelity | PASS — the person holds the commanded `(x, y)` to 2 dp. |
| Person parked **on the zebra crossing** `:J8_c0`, vehicle approaching on `E0` | PASS — `getRoadID()` became `:J8_c0`, and vehicle `f_0.0` **braked from 6.28 m/s to a full stop** at x = -34.3 and held. Vehicle-pedestrian interaction works. |
| Person parked **mid-carriageway** (jaywalking, lane `E0_1`) | FAIL — vehicles drive straight through. No reaction, no collision registered. |

**What this means for us:**

1. We do **not** need the delete-and-re-add hack, and we do **not** inherit the
   paper's crash bug. Straight `moveToXY` with `keepRoute=2` is enough.
2. We do **not** need Unity-side downward raycasting purely to resolve the SUMO
   edge — SUMO resolves it. (A raycast may still be wanted for *surface type*,
   e.g. "am I on the road or the sidewalk", for the social-force wall logic.)
3. **The jaywalking gap is real and still needs the paper's other fix**: to
   have vehicles see a pedestrian outside a crossing, the carriageway lanes must
   permit pedestrians (`allow` must include `pedestrian`). Without it a jaywalk
   study is meaningless because cars ignore the pedestrian entirely.

> Test scripts used for the above were run from a scratch directory and are not
> committed. Re-verify if the SUMO version changes.

### 8.4 Where this project actually stands today

An audit of the current code against the pedestrian goal:

**Backend (`RequiredFiles/sumo2unity/`)**
- MISSING: `core/sync_engine.py` only ever reads `traci.vehicle.getIDList()`.
  **There is no `traci.person` call anywhere in the outbound path**, so SUMO's
  pedestrians are never sent to Unity. This is the single biggest gap.
- PARTIAL: `core/actors.py` *does* have a `pedestrian` branch calling
  `person.moveToXY`, but it is dead code in practice: it only fires
  `if actor_id in active_person_ids`, and nothing ever creates that person in
  SUMO. There is no `traci.person.add()` call in the codebase.
- MISSING: `network/serializers.py` has no persons message builder.

**Unity (`Assets/_Project/Scripts/`)**
- PARTIAL: `SimulationController.cs` has an `isPedestrian` flag that flips the
  outbound `type` field to `"pedestrian"` — but `egoVehicleId` is still
  `f_0.0`, which is a `<trip type="EgoCar">` in the route file. **This is the
  known hack**: the XR rig drives a car object in SUMO while the human walks in
  Unity. It is why the tester shows up as a car.
- MISSING: no persons message handler, no pedestrian prefab, no walk animation,
  no NavMesh, no social-force code.
- PRESENT: `Assets/prefabs/XR Pedestrian.prefab` exists (XRI Origin rig with
  smooth locomotion) and is the right starting point for the human tester.

**3D network (`RoadNetworkBuilder.cs`)**
- PRESENT: zebra crossings are parsed (`//edge[@function='crossing']`) and built.
- MISSING: `RoadLaneData` carries no `allow`/`disallow` field, so a sidewalk
  lane is built as plain asphalt, at road height, with no curb and no way for
  other code to ask "is this walkable?".
- MISSING: walking areas (`function="walkingarea"`) are not built at all — 9
  exist in `Scenario1`.

**Scenario network (`Scenario1/`)** — measured *before* Phase 0:
- Only **1 crossing** in the entire network (`:J8_c0`, at junction J8).
- Sidewalks existed on **only 4 of 19 edges** (`E0`, `-E0`, `-E0.30`, `E00` —
  all width 2.00 m, all on the J8 corridor). `E5` and `E8` had **no sidewalk
  lanes at all**.
- **No traffic lights anywhere.** All 8 junctions are `type="priority"`; the
  network contains zero `<tlLogic>` blocks. The traffic-light plumbing in
  `SimulationController.ChangeTrafficStatus` is therefore inert in Scenario1,
  and pedestrian crossings here are **unsignalised** — vehicles yield by
  priority rule, not by phase.
- The route file declared `<personFlow id="pf_0" number="20">` from `E5` to
  `E8` — but because those edges had no sidewalks, and because 20 persons
  spread over 3600 s is one every 180 s, **a 300 s run spawned only 2
  pedestrians, and they never left `E8`**. They never reached the crossing.

So the pedestrian layer was absent end to end, and the scenario network could
not support it as authored. **Phase 0 (§8.8) has since fixed the network side.**

### 8.5 The Social Force Model

Origin and formulation, as the paper describes it:

- **Reynolds (1987)** — flocking. Each agent is a point with forces
  (separation, alignment, cohesion) summing to a velocity vector.
- **Helbing & Molnár (1995)** — adapted to pedestrian crowds with three social
  forces:
  1. **Desire** — pull toward the goal; a vector oriented at the destination.
  2. **Repulsion** — push away from nearby pedestrians; the sum of repulsion
     vectors from neighbours.
  3. **Attraction** — optional pull toward points of interest (a shop window, a
     companion, a street performer).

Each step, the forces sum to a movement vector that is applied to the agent.

**Implementation notes worth copying:**
- The paper's Unity implementation was ported from a NetLogo reference
  (`github.com/chraibi/SocialForceModel`, credited to Antoine Tordeux) and
  **omits the attraction force** — desire + repulsion only. That is the right
  scope for us too; attraction adds tuning burden with no benefit for a
  road-crossing study.
- They chose force-based over the more accurate **velocity-based** methods
  (time-to-collision, Karamouzas et al. 2017) purely on cost: velocity-based
  *"are more complex to implement and require more computational resources to
  run in real-time"*. In VR at 90 Hz that argument is even stronger for us.
- **Wall forces are mandatory, not optional.** Their first test had pedestrians
  pushed off the sidewalk into the road and out of the network. The fix: add
  repulsive "walls" along the edges of every walkable surface (sidewalks,
  walking areas, crossings); each step, an agent finds the closest point on a
  wall and gains a repulsive force away from it. As a last resort they
  hard-clamp the agent back inside the walkable polygon.
- **Known weakness to expect:** the paper reports that at high density,
  especially around the crossing, *"the age of the model becomes apparent, as
  some pedestrians are forced to wait for a chance to move forward, or even
  backtrack"*. Budget for a density cap or a queueing tweak at crossings.

**Their validated scale (our target to beat):** 100 pedestrians spawned
initially, +5/minute, all simulated in Unity, with SUMO handling only cars —
running acceptably on an i7-8750H / RTX 2060 / 16 GB with an Oculus Rift. Our
target hardware is comparable, so 100 concurrent agents is a realistic goal.

### 8.6 Implementation plan

Six phases, ordered so each one is independently testable. Phases 1-3 deliver
the user-visible goal (NPC pedestrians walking and crossing, plus a real XR
pedestrian); 4-6 are quality and rigour.

---

#### Phase 0 — Fix the scenario network ✅ **DONE** — see §8.8

Regenerated `Scenario1` with full pedestrian infrastructure and rewrote the
pedestrian demand. Results, exact commands and the Unity follow-up step are
recorded in §8.8.

---

#### Phase 1 — SUMO to Unity pedestrian channel ✅ **DONE** — see §8.9

Make SUMO's existing pedestrians visible in Unity. Pure addition; touches no
vehicle code.

*Backend:*
- `network/serializers.py`: add `build_persons_message(persons_list)` emitting
  `{"type": "persons", "persons": [...]}` (schema in §4).
- `network/zmq_bridge.py`: add `send_persons()` alongside `send_vehicles()`.
- `core/sync_engine.py`: in `_run_loop`, after the vehicle extraction block, add
  a person extraction block — iterate `traci.person.getIDList()`, apply the same
  `subscribe_radius` filter against `ego_pos`, and read `getPosition3D`,
  `getAngle`, `getSpeed`, `getTypeID`, `getRoadID`.
  - Derive a `state` field from `getRoadID()`: a road id starting with `:` and
    containing `_c` is a crossing, `_w` a walking area, otherwise sidewalk.
    Unity uses this to pick the animation and, later, to colour crossing
    analytics.
- Add `person_count` to the `on_step_callback` dict so the GUI telemetry row can
  show it.

*Unity:*
- `SimulationController.cs`: add a `[Serializable] Person` class and
  `PersonWrapper`, then a `common.type == "persons"` branch in `HandleMessage`,
  mirroring the vehicles branch: spawn on first sight, update on subsequent
  frames, destroy when an id drops out of the incoming set.
- Add a `PedestrianController.cs` — the pedestrian analogue of
  `VehicleController.cs`, but **simpler**: pedestrians need no rigidbody
  physics, no angular-velocity blending, no residual spin. Lerp position, slerp
  rotation, and drive an `Animator` float from `speed` to blend idle to walk.
- Add a `pedestrianPrefab` field plus a `List<PedestrianModel>` type-to-prefab
  map, matching the existing `carModelsList` pattern. **A humanoid pedestrian
  model and walk animation must be sourced — the project has none today.**

**Done when:** pressing Play shows SUMO's `pf_*` pedestrians walking the
sidewalks in Unity, and stopping at the crossing when the light is against them.

---

#### Phase 2 — Make the XR tester a real SUMO pedestrian ✅ **DONE** — see §8.10

This is the fix for the bug described in §8.4.

*Backend:*
- `core/actors.py` — add person lifecycle, the piece that is missing entirely:
  - On first sight of an actor with `actor_type == "pedestrian"` whose id is not
    in `traci.person.getIDList()`, call
    `traci.person.add(pid, edge, pos, typeID="DEFAULT_PEDTYPE")` followed by
    `traci.person.appendWalkingStage(pid, [edge], arrivalPos)`. **A person with
    no stage is removed by SUMO immediately** — the walking stage is what keeps
    it alive; it is a keep-alive, not a route we intend to follow.
  - Then drive it every step with the existing
    `person.moveToXY(..., keepRoute=2)` call, which §8.3 confirms is sufficient.
  - Bootstrap edge: resolve from the XR rig's spawn position via
    `traci.simulation.convertRoad(x, y, isGeo=False, vClass="pedestrian")`.
- `config.py`: add `ego_is_pedestrian: bool` and `ego_person_id: str` (default
  `"xr_ped"`) so the ego id is no longer forced to be a vehicle trip.
- `gui/app.py`: expose an "Ego is pedestrian" checkbox.

*Unity:*
- `SimulationController.cs`: when `isPedestrian` is true, stop registering the
  ego in the vehicle dictionary as a car. Send `egoVehicleId = "xr_ped"` (not
  `f_0.0`), keep `type = "pedestrian"`.
- Remove the `EgoCar` trip dependency for pedestrian mode. `f_0.0` should stay
  in the route file only for driving experiments.
- Speed: the current `CollectVehicleData` falls back to positional differencing
  when there is no `Rigidbody` — correct for an XR rig; keep it, but clamp to a
  sane pedestrian max (~2.5 m/s) so a teleport or recentre does not emit a
  40 m/s spike into SUMO.

**Done when:** with the headset on, SUMO-GUI shows a **person** triangle, not a
car, tracking the tester; and stepping onto the zebra crossing makes approaching
SUMO vehicles brake — which §8.3 confirms works.

---

#### Phase 3 — Social Force Model for Unity-side NPC pedestrians

Now offload pedestrian simulation from SUMO to Unity, per the paper.

- `SocialForceAgent.cs` — per-agent, each step:
  - **Desire force** toward the next waypoint.
  - **Repulsion force** = sum over neighbours within a radius. Use a spatial
    hash or `Physics.OverlapSphereNonAlloc` on a dedicated Pedestrian layer; a
    naive O(n^2) loop will not hold 100 agents at 90 Hz.
  - **Wall force** from the nearest point on the walkable-surface boundary
    (Phase 4 supplies these), plus a hard clamp back inside as the last resort.
  - Skip attraction, as the paper did.
- `PedestrianSpawner.cs` — mirrors the paper: an initial count plus a per-minute
  rate, random origin/destination on the sidewalk graph, despawn on arrival.
- **Crossing discipline** — the paper does not solve this, and it is what we
  actually care about. Scenario1 is unsignalised (§8.8), so agents must judge a
  **gap in traffic** at the kerb rather than read a signal; keep the
  `_lastTlState` path as a branch for scenarios that do have lights. Expose a
  `jaywalkProbability` so risk behaviour can be studied deliberately — this is
  named as future work in the paper's conclusion and is a genuine contribution
  for us.
- **Push back to SUMO:** extend the Unity to Python message to carry an array of
  agents, not just the ego. `ActorManager.update_from_unity` already loops over
  a list, so the backend largely supports this — but the Unity side currently
  sends exactly one entry (`new Vehicle[] { egoVehicleData }`).
- **Hand-off rule:** when Unity simulates pedestrians, SUMO must not also spawn
  them. Add a config flag that switches between *SUMO-authoritative* (Phase 1)
  and *Unity-authoritative* (Phase 3) pedestrians, and strip `personFlow` from
  the route file in the latter.

**Done when:** ~100 Unity-simulated pedestrians walk, avoid each other visibly,
queue at the crossing, and SUMO vehicles yield to them.

---

#### Phase 4 — Walkable surfaces in 3D

Required by Phase 3's wall forces and by simple visual credibility.

- `RoadNetworkData.RoadLaneData`: add `allow` / `disallow` string fields, parsed
  in `RoadNetworkBuilder.LoadSumoXmlFiles`.
- Build sidewalk lanes as **raised** meshes with a kerb (a vertical skirt down
  to road level) and a sidewalk material — the textures already exist unused at
  `Assets/_Project/Textures/SidewalkMaterial*.png|jpg`.
- Build walking-area polygons from `function="walkingarea"` edges, using the
  `outlineShape` attribute where present (the crossing parser already reads it).
- Tag every walkable mesh onto a `Walkable` layer and emit its boundary polygon
  into a shared list for the social-force wall term.
- Bake a **NavMesh** over walkable surfaces, so agents get global pathfinding
  and social force only handles local avoidance — this is the standard split and
  it is cheaper than making social force do both.
- Add sidewalk and walking-area polygons to `GrassField.exclusionPolygons`, or
  grass will grow through the pavement (see §7.3).

---

#### Phase 5 — Analytics & validation

- Extend `analytics/metrics_logger.py` and the Unity `vehicle_data_report.txt`
  writer to log pedestrians: id, position, speed, `road_id`, and crossing
  entry/exit timestamps.
- **Safety metrics** the paper never computed, and the real research payload:
  time-to-collision between pedestrian and vehicle at the crossing, vehicle
  yield rate, pedestrian wait time at the kerb, and gap acceptance.
- Feed the existing `EyeGazeTracker` / `EyeDataLogger` into the same timeline so
  gaze can be correlated with crossing decisions.
- Validate against the paper's own caveat about VR realism: Bhagavathula et al.
  (2018) found *"only minor differences"* between real and virtual pedestrian
  behaviour — cite this when defending the method.

---

### 8.7 Risks and open questions

- **No humanoid asset.** `Assets/_Project/Resources/` has cars, buildings,
  trees, signs and lamps — no people. A rigged humanoid plus an idle/walk blend
  tree is a hard prerequisite for Phase 1 and is not yet sourced.
- **Message-rate cost.** Phase 3 sends ~100 agent poses per step at 10 Hz in
  *both* directions. The current Unity sender rebuilds one JSON string per frame
  via `JsonUtility`; at 100 agents this needs measuring, and probably a cached
  `StringBuilder` or a binary frame.
- **`JsonUtility` cannot deserialise a bare array**, which is why the existing
  `JsonHelper` wraps things. Keep the persons message wrapped in an object.
- **Density degradation is expected**, not a bug — see §8.5. If crossing queues
  look wrong, compare against the paper's reported behaviour before assuming a
  defect in our port.
- **Two pedestrian authorities can conflict.** If both SUMO `personFlow` and the
  Unity spawner run at once, ids will collide and cars will react to phantom
  pedestrians. The Phase 3 config flag is not optional.
- **~~Traffic light phases may not include a pedestrian phase.~~** *Resolved in
  Phase 0:* Scenario1 has **no traffic lights at all** — all junctions are
  `type="priority"`. Crossings are unsignalised and vehicles yield by priority
  rule, which is verified working (§8.8). Unity's traffic-light code is inert
  in this scenario. Phase 3 crossing discipline must therefore be based on
  **gap acceptance**, not on `_lastTlState`.

---

### 8.8 Phase 0 — Completed: Scenario 1 pedestrian network

**Status: done.** `Scenario1` now has sidewalks on every edge, crossings at
every junction, walking areas, and a pedestrian demand that actually uses the
zebra crossing at J8.

### What changed

Backups of the three original files are in `Scenario1/_backup_pre_phase0/`.

**`Sumo2Unity.net.xml`** — regenerated with:

```
netconvert -s Scenario1/Sumo2Unity.net.xml \
  --sidewalks.guess true --sidewalks.guess.max-speed 14.0 \
  --default.sidewalk-width 2.0 \
  --crossings.guess true --crossings.guess.speed-threshold 14.0 \
  --default.crossing-width 4.0 \
  --walkingareas true \
  --no-turnarounds true --offset.disable-normalization true \
  --junctions.corner-detail 5 --junctions.limit-turn-speed 5.50 \
  --rectangular-lane-cut false --geometry.avoid-overlap false \
  -o Scenario1/Sumo2Unity.net.xml
```

The trailing options replicate the settings in `Sumo2Unity.netecfg` so the
geometry stays as close to the netedit original as possible. **Re-use this exact
command** if the network is ever regenerated, or the sidewalks will be lost.

| Metric | Before | After |
|---|---|---|
| Edges with a sidewalk | 4 of 19 | **19 of 19** |
| Pedestrian crossings | 1 (`:J8_c0`) | **14** |
| Walking areas | 9 | **20** |
| `netOffset` / `convBoundary` | `0,0` / `-76.54,-32.31,72.60,35.13` | **unchanged** |

**`Sumo2Unity.rou.xml`** — added three pedestrian `vType`s (`ped_adult`,
`ped_slow`, and `ped_xr` reserved for the Phase 2 VR tester) and replaced the
single ineffective `pf_0` flow with six person flows: three that cross the J8
zebra, two that walk the corridor without crossing, and one around J1. Total
pedestrian demand is 1020/hour. Vehicle flows and the `f_0.0` ego trip are
unchanged.

The crossing flows carry explicit `departPos` / `arrivalPos` values
(`18.00` / `4.00`). **This matters and should not be removed:** with SUMO's
default positions, both trip ends land at junction J0, so pedestrians crossed
at J0 and only 13 ever used the J8 zebra. Anchoring the trip ends near J8 moved
that to 30 pedestrians and eliminated J0 crossings entirely.

### Verified results (300 s headless run, measured not assumed)

| Check | Before | After |
|---|---|---|
| Distinct pedestrians spawned | 2 | **85** |
| Peak concurrent pedestrians | 0–1 | **18** |
| Pedestrians using the J8 zebra | 0 | **30** |
| Pedestrians using any crossing | 0 | **48** |
| Vehicles yielding at the J8 zebra | n/a | **8 distinct**, stopping 0.9–1.0 m short |
| Warnings / errors over 600 s | — | **none** |
| Teleports | — | **0** |
| Ego route `f_0.0` still valid | yes | **yes** — `E0 → :J8_2 → E00 → … → E4` |

The ego car's route passes directly through the J8 crossing, so a driving
subject will meet crossing pedestrians without any further scenario work.

### Road alignment is preserved — your scene props are safe

Adding sidewalks normally shifts carriageways sideways, which would leave every
manually-placed building, tree and street lamp misaligned. Measured worst-case
lateral shift of the carriageway centreline across all edges: **0.008 m**.

Lane *endpoints* did move — junctions grew or shrank by 2–5 m to make room for
crossings and walking areas — but the centrelines did not. Buildings, trees and
grass will still line up after the Unity rebuild.

### ⚠️ Required manual step — rebuild the road network in Unity

The Unity scene still holds the **old** geometry. Until it is rebuilt there are
no sidewalk or crossing meshes for pedestrians to stand on. In Unity:

1. Open the `Scenario1` scene.
2. Select the **Managers** GameObject and confirm **Road Network Builder** points
   at the `Scenario1` folder.
3. Menu **Sumo2Unity → 1. Create Road Network**.
4. Let it finish, then **save the scene**.

Expect the rebuild to produce more geometry than before (19 sidewalk lanes and
14 crossings rather than 4 and 1). Note the known cosmetic limits, which Phase 4
addresses:

- Sidewalks build as **flat asphalt at road height with no kerb** —
  `RoadLaneData` still has no `allow`/`disallow` field, so the builder cannot
  tell a sidewalk from a carriageway.
- **Walking areas are not built at all.** There will be visible gaps at junction
  corners where pedestrians appear to walk on nothing.
- Grass exclusion only covers lanes, junctions and crossings, so blades may
  poke through the new sidewalks until Phase 4.

Pedestrians will still walk correctly in SUMO regardless — this is a visual gap
in Unity, not a simulation one.

### Notes and deliberate decisions

- **No traffic lights were added.** Scenario1 has none, and the J8 crossing works
  unsignalised: vehicles yield by priority rule, which the test above confirms.
  This also matches the paper's Fig. 6 (a SUMO vehicle waiting for the immersed
  user). If a signalised crossing is wanted later, regenerate with `--tls.guess
  --tls.crossing-min.time 6`, but be aware it changes vehicle behaviour
  network-wide and the Unity traffic-light heads must then match the new
  programme.
- **Jaywalking is still not modelled.** Per §8.3, vehicles ignore pedestrians on
  the carriageway. Enabling it means adding `pedestrian` to the carriageway
  `allow` lists, which should be done on a **separate scenario copy** because it
  changes vehicle behaviour everywhere. Deferred to Phase 3, where
  `jaywalkProbability` lives.
- `Sumo2Unity.Poly.xml` was not touched; building and terrain polygons are
  unaffected.
- Scenario2 was not modified. Applying the same `netconvert` command there is
  straightforward if that scenario is ever needed for pedestrian work.

---

### 8.9 Phase 1 — Completed: SUMO → Unity pedestrian channel

**Status: code done and verified end to end.** SUMO's pedestrians are now
published to Unity and spawned there. The only thing missing is art: without a
humanoid prefab assigned, pedestrians draw as capsules (see the setup steps
below).

#### Backend changes

| File | Change |
|---|---|
| `network/serializers.py` | `build_persons_message()` — emits `{"type":"persons","persons":[…]}`. Kept separate from the vehicles message so Unity never runs a person through `VehicleController`. |
| `network/zmq_bridge.py` | `send_persons()`, alongside `send_vehicles()`. |
| `core/sync_engine.py` | New step 5b: iterates `traci.person.getIDList()`, applies the same `subscribe_radius` filter as vehicles, reads position/angle/speed/type/road. Plus `classify_person_road()`. |
| `gui/app.py` | New **PEDESTRIANS** telemetry card showing `in-radius / total`. |

`classify_person_road()` turns a SUMO road id into the surface the pedestrian is
standing on, so Unity does not have to re-derive it from geometry:

| Road id | `state` |
|---|---|
| `:J8_c0` | `crossing` |
| `:J8_w0` | `walkingarea` |
| `E0`, `-E0.30` | `sidewalk` |
| `:J1_13` | `internal` |

This field is what Phase 3 crossing behaviour and Phase 5 crossing analytics
will key off, so it is worth keeping accurate.

#### Unity changes

**`PedestrianController.cs`** (new) — the pedestrian analogue of
`VehicleController`, deliberately much simpler. A car needs a Rigidbody,
local-axis velocities and angular-velocity blending because its heading and
travel direction differ. A pedestrian always walks where it faces and never
needs to collide with anything, because SUMO already resolved that; driving it
through physics would only fight the incoming positions. It interpolates
position and heading with `1 - exp(-k·dt)` — frame-rate independent, which
matters in VR where frame times vary far more than on a flat screen — snaps
rather than slides when SUMO re-inserts a person, and drives an `Animator`
float from walking speed.

**`SimulationController.cs`** — added `Person` / `PersonWrapper` /
`PedestrianModel` types, a `persons` branch in `HandleMessage`, and
`HandlePersonsMessage` + `SpawnPedestrian`. Pedestrians live in their own
`personObjects` dictionary, separate from `vehicleObjects`. New inspector
fields: `pedestrianPrefab`, `pedestrianModelsList`, `pedestrianHeadingOffset`.

Three details worth knowing:

- **Heading offset is 0 for pedestrians, not −90.** Vehicles apply `angle - 90`
  because the car models face **+X**. A standard humanoid (Unity's and Mixamo's
  convention) faces **+Z**, and SUMO's angle convention — 0° = north, 90° = east,
  clockwise — maps straight onto Unity's Y euler. If an imported character faces
  sideways, correct it with `pedestrianHeadingOffset` rather than rotating the
  prefab root, which would fight the controller.
- **The ego pedestrian is skipped.** When `isPedestrian` is true, the person
  whose id matches `egoVehicleId` is ignored, so the XR tester is never given a
  SUMO-driven puppet standing inside the headset (Phase 2).
- **Capsule fallback.** With no prefab assigned, a scaled capsule is spawned and
  its collider stripped. This made the whole channel testable before any art
  existed, and is why "it works but they are capsules" is the expected first run.

#### Verified

Ran the real `SyncEngine` against `Scenario1` with a ZeroMQ subscriber standing
in for Unity:

| Check | Result |
|---|---|
| Message types published | `vehicles` ×200, **`persons` ×199** |
| Distinct pedestrians over the wire | 10 |
| Surface states observed | `sidewalk` 638, `walkingarea` 383, `crossing` 210 |
| GUI telemetry | `active_persons=8`, `total_sumo_persons=8` |
| C# compile (Roslyn, Unity 6000.0.53f1 refs) | **0 errors** |

Sample payload:

```json
{"person_id":"pf_j1.0","position":[2.02,16.35,0.0],"angle":195.04,
 "type":"ped_adult","speed":1.19,"road_id":":J1_w0","state":"walkingarea"}
```

#### ⚠️ Required manual step — assign a pedestrian prefab

Until this is done pedestrians render as capsules. On the **Managers** object,
under **Simulation Controller → Pedestrians**:

1. Set **Pedestrian Prefab** to a rigged humanoid (the fallback for any vType).
2. Optionally fill **Pedestrian Models List** to map a SUMO vType to a specific
   prefab — `ped_adult` and `ped_slow` are the two in use.
3. Leave **Pedestrian Heading Offset** at 0 unless the character faces sideways.

The prefab needs an `Animator` with a **float parameter named `Speed`** driving
an idle→walk blend tree. `PedestrianController` checks that the parameter exists
before writing to it, so a prefab without one still moves correctly — it just
does not animate, and does not spam the console.

#### Importing a Mixamo character into this URP project

This project renders through **URP** (`Assets/Settings/PC_RPAsset.asset`). A
Mixamo FBX arrives with its materials and textures as **embedded sub-assets**
(`materialLocation: InPrefab`, `externalObjects: {}`), and those materials are
authored for the built-in pipeline. Embedded materials cannot be edited or
upgraded in place, so the character imports untextured: **magenta** if the
shader is built-in Standard, **flat grey/white** if the shader resolved to URP
but no texture bound.

Fix, on the character FBX (not the animation FBX):

1. Select the FBX → Inspector → **Materials** tab.
2. **Extract Textures…** → the character's own folder. Writes the real PNGs.
3. **Extract Materials…** → same folder. Writes editable `.mat` files and fills
   in `externalObjects`.
4. If the extracted materials show shader **Standard**:
   **Window → Rendering → Render Pipeline Converter → Built-in to URP →
   Material Upgrade**. Or set each shader to `Universal Render Pipeline/Lit`.
5. Bind the maps — URP/Lit slot names differ from Mixamo's file names:
   **Base Map** ← `*_Diffuse`, **Normal Map** ← `*_Normal`,
   **Metallic/Specular** ← `*_Specular`.
6. Select every extracted `*_Normal.png` → **Texture Type: Normal map** → Apply.
   Without this URP renders the normals as flat colour.
7. Hair uses `*_Opacity`: on that material enable **Alpha Clipping** (cheaper in
   VR) or set Surface Type **Transparent**, or the hair renders as a solid slab.

Two layout notes from the first import:

- A folder literally named `Pedestrian.prefab` is not a prefab. Unity shows it
  as a folder and the real asset ends up at `Pedestrian.prefab/Remy.prefab`.
  Harmless, but rename the folder to something like `Prefabs`.
- Pedestrian art does **not** need to live under `Resources/`.
  `SimulationController` uses the Inspector reference, not `Resources.Load`, so
  anything under `Resources/` is force-included in every build for no reason -
  and a Mixamo FBX with embedded textures is ~28 MB.

#### Known limits, deferred by design

- **No ground raycast.** Height comes straight from SUMO's z (0 everywhere here)
  plus `yOffset`. Fine while sidewalks are flat; once Phase 4 raises them onto
  kerbs, pedestrians will need to sample the surface.
- **No pedestrian LOD or culling** beyond the `subscribe_radius` filter. The
  paper ran 100 agents on comparable hardware, so this should hold, but it is
  untested in VR here.
- Pedestrians are **destroyed and respawned** when they leave and re-enter the
  subscription radius, which resets their animation phase.

---

### 8.10 Phase 2 — Completed: the XR tester is a real SUMO pedestrian

**Status: done and verified.** The car hack described in §8.4 is gone. With
"Ego is XR pedestrian" ticked, the VR subject is injected into SUMO as a
**person**, and SUMO vehicles yield to it at the crossing.

#### The person lifecycle, and why the walking stage exists

SUMO has no free-floating person: one must be created **on an edge**, and a
person with **no remaining stage is deleted at the end of the step it was
created in**. So `ActorManager._create_person` does three things:

1. `simulation.convertRoad(x, y, vClass="pedestrian")` — turns the XR rig's
   world position into the nearest pedestrian edge and lane position.
2. `person.add(id, edge, pos, typeID="ped_xr")`.
3. `person.appendWalkingStage(id, [edge], 1.0)` — a one-metre stage appended
   **purely as a keep-alive**. It is never walked, because `moveToXY` overrides
   the position every step.

Verified by holding a person alive through **120 s of continuous `moveToXY`**:
it survived, `getRemainingStages` never dropped to 0, and the road tracked
correctly `E0 → :J8_2 → :J8_c0 → :J8_w0`.

Creation is retried each step until it succeeds, so a subject who starts off the
network simply gets injected once they step onto a pedestrian edge.
`_note_create_failure` logs the first failure per actor at INFO and the rest at
DEBUG, so standing off-network does not flood the console.

#### Changes

| File | Change |
|---|---|
| `config.py` | `ego_is_pedestrian`, `ego_person_type` (default `ped_xr`). |
| `core/actors.py` | `_create_person()` + `_note_create_failure()`; the `pedestrian` branch now **creates** the person instead of silently skipping when absent. This is what made the old branch dead code. |
| `core/sync_engine.py` | Ego position for the radius filter now reads `traci.person` when the ego is a pedestrian; the XR subject is excluded from the outbound `persons` message. |
| `core/sumo_manager.py` | SUMO-GUI follows a pedestrian ego via `gui.track()`; `gui.trackVehicle()` only accepts vehicles, so the camera previously just never followed. |
| `gui/app.py` | **Ego is XR pedestrian** checkbox and an **Ego ID** field. Toggling swaps the id between `f_0.0` and `xr_ped`, but only when the field still holds the other mode's default, so a hand-typed id is never clobbered. |
| `SimulationController.cs` | `maxPedestrianSpeed` clamp (default 6 m/s). |

The speed clamp matters: an XR rig has no Rigidbody, so speed is differenced
from camera position. A recentre, teleport or dropped frame moves the camera
metres in one step, which would report tens of m/s into SUMO's telemetry.

#### Verified

| Check | Result |
|---|---|
| XR ego exists in SUMO as a **person** | ✅ `type=ped_xr`, injected on edge `E0` at pos 9.54 |
| XR ego wrongly exists as a **vehicle** | ✅ no |
| Road tracks as the subject walks and crosses | ✅ `E0 → :J8_2 → :J8_c0 → :J8_w0` |
| **Not** echoed back to Unity as an NPC | ✅ false |
| Other pedestrians still stream to Unity | ✅ 15 distinct |
| Vehicles halt while the XR subject stands on the zebra | ✅ 10 |
| Telemetry `active_persons` / `ego_speed` | ✅ 10 / 1.3 |
| C# compile | ✅ 0 errors |

#### ⚠️ Manual step — switching the scene to pedestrian mode

On **Managers → Simulation Controller**:

1. Tick **Is Pedestrian**.
2. Set **Ego Vehicle Id** to `xr_ped` — a **person id**, not a trip from the
   route file. Leaving it at `f_0.0` recreates the original bug, because the
   backend would inject a person named after a car trip.
3. Tick **Is Scene Object** and set **Ego Vehicle** to the `XR Pedestrian` rig,
   so Unity moves the existing rig instead of cloning it.
4. Leave **Max Pedestrian Speed** at 6.

In the Python dashboard, tick **Ego is XR pedestrian**; the Ego ID field
switches to `xr_ped` on its own.

`f_0.0` stays in the route file for driving experiments — the two modes coexist,
you just pick one per run.

#### Harmless SUMO warnings you will see

Both are expected for an externally driven pedestrian and neither breaks
anything:

- `Person 'xr_ped' entered crossing lane ':J8_c0_0' without registering approach`
  — the subject stepped onto the zebra without SUMO's normal approach protocol,
  which is exactly what a teleporting external controller does. Vehicles still
  yield; that is measured above.
- `Could not map position … onto lane ':J8_w0_0'` — occasional walking-area
  mapping misses. The position is still held exactly.

#### Known limit — the road id goes stale off-network

If the subject walks well off any pedestrian edge (easy in room-scale VR),
`moveToXY` keeps the exact position but `getRoadID` **keeps returning the last
matched road**. Measured: standing 60 m off-network still reported `:J8_c0`.

Traffic is *not* affected — SUMO yields on true geometry, so 22 vehicles passed
at speed while the subject was 60 m away, versus 2 stopping for unrelated
pedestrians. But it does mean the `state` field is unreliable for the ego when
it is off-network, which matters for Phase 5 crossing analytics: derive the
subject's crossing state from Unity-side geometry, not from SUMO's road id.

---

### 8.11 Desktop pedestrian — testing Phase 2 without a headset

A keyboard/mouse stand-in for the XR rig, so the pedestrian co-simulation can be
driven and tested with no VR hardware attached. Everything downstream is
identical: SUMO still sees a `ped_xr` person, and vehicles still yield.

#### Creating it

Menu **Sumo2Unity → 4. Create Desktop Test Pedestrian**.

That builds the rig, saves it to `Assets/_Project/Prefabs/DesktopPedestrian.prefab`,
places it in the scene, and wires `SimulationController` automatically:
`Is Pedestrian = true`, `Is Scene Object = true`, `Ego Vehicle = the rig`,
`Ego Vehicle Id = xr_ped`.

The prefab is **generated by an editor script rather than committed as
hand-written YAML**, so Unity itself serialises the Camera and the component
references. Hand-authored prefab YAML is the kind of thing that loads fine until
it doesn't.

Then in the Python dashboard tick **Ego is XR pedestrian** (the Ego ID field
switches to `xr_ped` on its own), start the backend, and press Play.

#### Controls

| Input | Action |
|---|---|
| **W A S D** | Walk |
| **Mouse** | Look. Yaw turns the body, so SUMO gets the heading you are facing |
| **Shift** | Run (3.0 m/s) |
| **Ctrl** | Slow (0.6 m/s), for edging up to a kerb |
| **Esc** | Release the cursor |
| **Click** | Recapture the cursor |

Default walk speed is **1.4 m/s**, matching the `ped_adult` vType in the route
file, and run stays under the 6 m/s clamp `SimulationController` applies to a
pedestrian ego.

#### Where it spawns, and what to try

Spawns at Unity `(-45, 0, 6.27)` facing **east**, which is the `E0` sidewalk west
of the J8 zebra. Verified: `convertRoad` maps it to edge `E0`, pos 9.49, lane 0
— a real sidewalk lane, so the SUMO person injects immediately.

To reproduce the Phase 2 result by hand:

1. Hold **W** for about 10 s (13.7 m at walking pace) to reach the kerb.
2. Turn left and step onto the zebra — SUMO road becomes `:J8_c0`.
3. Watch traffic on `-E0` brake and stop about a metre short of you.

Verified mapping along that route: `E0` → `:J8_2` → `:J8_c0` → `:J8_0`.

#### Design notes

- **Plain transform walker, not a CharacterController.** The generated road
  network has no guaranteed colliders, and a CharacterController with nothing to
  stand on falls through the world. Ground following is an optional raycast that
  **keeps the last good height when it hits nothing**, so it works whether or not
  the meshes have colliders.
- **Root on the ground, camera child at 1.7 m**, mirroring an XR Origin.
  `SimulationController` reports the *root* position to SUMO, so the pedestrian
  must be standing on the pavement rather than floating at eye height.
- **Pitch is camera-only.** Tilting the root would tilt the heading sent to SUMO
  and lift the body off the ground plane.
- Uses the **legacy `Input` API**, which is valid here because Project Settings
  has `activeInputHandler: 2` ("Both"). No Input Actions asset needed, so the rig
  has no package dependency.
- The camera is tagged **MainCamera** deliberately — see §7.3: `Scenario1` had
  none, which meant grass never generated in play mode. The setup script warns if
  other cameras are already tagged MainCamera, since `Camera.main` then picks one
  arbitrarily; disable the XR rig or ego car camera while testing on desktop.

#### Switching back to real XR

Set `SimulationController.Ego Vehicle` back to the `XR Pedestrian` rig and
disable the desktop rig. Everything else — `Is Pedestrian`, `Ego Vehicle Id`,
the backend checkbox — stays the same, because the desktop rig is only a
different way of moving the same transform.
