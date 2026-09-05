// ==============================
// GrassField.cs
// ==============================
// Procedural grass over the terrain polygons.
//
// Scenario2's terrain is ~813,000 m2. At any believable density that is tens of
// millions of blades, so baking grass into the scene is not an option. Instead
// the field is generated in cells around the camera and thrown away behind it:
// cost is bounded by viewRadius, not by how big the map is.
//
// Cells are marked HideAndDontSave, so grass never enters the saved scene and
// cannot bloat the scene file. It regenerates on load and on entering play mode.
//
// Placement is deterministic: a blade's position comes from a hash of its cell
// and index, so a cell rebuilt after the camera returns is identical and grass
// does not visibly reshuffle.
//
// Every parameter lives in GrassSettings, authored on the RoadNetworkBuilder
// manager and pushed down by it, so there is one place to tune grass.
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every knob the grass has, in one serializable block so it can be authored in
/// a single place on the RoadNetworkBuilder manager and pushed into the field.
/// </summary>
[System.Serializable]
public class GrassSettings
{
    [Tooltip("Turn grass off entirely.")]
    public bool enabled = true;

    [Tooltip("Leave empty to auto-load Assets/_Project/Materials/Mat_Grass.mat.")]
    public Material material;

    [Header("Blades")]
    [Tooltip("Blades per square metre. Cost scales linearly with this.")]
    [Range(1f, 40f)] public float density = 10f;

    [Tooltip("Average blade height in metres. From a 1.2 m eye height, hiding a " +
             "road 10 m away needs 0.60 m, 20 m away needs 0.90 m, 30 m away " +
             "needs 1.00 m. Drop to ~0.35 for a mown verge instead.")]
    public float height = 0.9f;

    [Tooltip("Fraction of height randomised per blade.")]
    [Range(0f, 0.8f)] public float heightVariation = 0.35f;

    [Tooltip("Blade width in metres at the base.")]
    [Range(0.005f, 0.2f)] public float width = 0.045f;

    [Header("Extent")]
    [Tooltip("Grass is generated only within this distance of the camera. " +
             "Cost scales with the square of this.")]
    public float viewRadius = 32f;

    [Tooltip("Size of one generated cell. Larger means fewer draw calls but " +
             "coarser culling and bigger rebuild hitches.")]
    public float cellSize = 8f;

    [Tooltip("Safety cap on live cells, so a bad radius cannot lock up the editor.")]
    public int maxCells = 256;

    [Tooltip("Pushes the grass circle ahead of the camera, as a fraction of View " +
             "Radius. Grass behind the car cannot be seen while driving, so this " +
             "spends the same number of cells on ground you are looking at. " +
             "0 = centred on the camera; 0.5 = half a radius ahead, giving 1.5x " +
             "the forward reach for no extra cost.")]
    [Range(0f, 0.9f)] public float forwardBias = 0.5f;

    [Tooltip("Seconds for the bias direction to catch up. Without this a quick " +
             "head turn in VR would shift the whole field at once and force every " +
             "cell to regenerate in one frame.")]
    [Range(0f, 3f)] public float biasSmoothing = 0.6f;

    [Tooltip("Above this speed the bias follows travel direction rather than gaze, " +
             "so looking around while driving does not drag the grass with it.")]
    public float biasSpeedThreshold = 1.5f;

    [Header("Colour")]
    public Color baseColor = new Color(0.16f, 0.28f, 0.10f);
    public Color tipColor = new Color(0.42f, 0.60f, 0.24f);
    [Range(0f, 1f)] public float colorJitter = 0.18f;

    [Header("Placement")]
    [Tooltip("Keep grass this far back from road and junction edges.")]
    public float roadMargin = 0.25f;

    [Tooltip("Sink blade roots this far below the terrain surface.")]
    public float rootSink = 0.02f;

    [Header("Wind and Shading")]
    [Range(0f, 0.5f)] public float windStrength = 0.08f;
    [Range(0f, 5f)] public float windSpeed = 1.2f;
    [Range(0.01f, 1f)] public float windScale = 0.12f;
    [Tooltip("Cheap translucency, so backlit grass glows instead of reading as " +
             "a dark mass.")]
    [Range(0f, 1f)] public float backlight = 0.35f;
    [Range(0f, 1f)] public float smoothness = 0.15f;

    public GrassSettings Clone() => (GrassSettings)MemberwiseClone();
}

/// <summary>A closed 2D polygon in Unity XZ space.</summary>
[System.Serializable]
public class GrassPolygon
{
    public Vector2[] points;

    public GrassPolygon() { }
    public GrassPolygon(Vector2[] pts) { points = pts; }
}

[ExecuteAlways]
[DisallowMultipleComponent]
public class GrassField : MonoBehaviour
{
    [Tooltip("Every value here is authored on the RoadNetworkBuilder manager " +
             "object and pushed down; editing it there updates grass live.")]
    public GrassSettings settings = new GrassSettings();

    // Baked by RoadNetworkBuilder: where grass may grow, and what it must avoid.
    [HideInInspector] public List<GrassPolygon> terrainPolygons = new List<GrassPolygon>();
    [HideInInspector] public List<GrassPolygon> exclusionPolygons = new List<GrassPolygon>();
    [HideInInspector] public float terrainY;

    private readonly Dictionary<Vector2Int, GameObject> _cells = new Dictionary<Vector2Int, GameObject>();
    private readonly List<Vector2Int> _scratchRemove = new List<Vector2Int>();

    // Exclusion polygons bucketed by cell, so a blade only tests the handful of
    // road pieces near it instead of every road in the city.
    private Dictionary<Vector2Int, List<int>> _exclusionIndex;
    private Rect[] _exclusionBounds;
    private float _indexCellSize;

    private Vector3 _lastBuildPos = new Vector3(float.MaxValue, 0f, float.MaxValue);

    // Smoothed heading the field leans towards, plus the previous camera position
    // used to estimate travel direction.
    private Vector3 _focusDir = Vector3.forward;
    private Vector3 _prevCamPos;
    private bool _hasPrevCamPos;

    // Shading is pushed per renderer rather than onto the shared material, so
    // tweaking wind in the manager never edits the material asset on disk.
    private MaterialPropertyBlock _propertyBlock;
    private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindScaleId = Shader.PropertyToID("_WindScale");
    private static readonly int BacklightId = Shader.PropertyToID("_Translucency");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private void OnEnable() => Rebuild();
    private void OnDisable() => ClearCells();

    private void OnValidate()
    {
        settings.density = Mathf.Max(0.1f, settings.density);
        settings.cellSize = Mathf.Max(2f, settings.cellSize);
        settings.viewRadius = Mathf.Max(settings.cellSize, settings.viewRadius);
        settings.height = Mathf.Max(0.05f, settings.height);
        _lastBuildPos = new Vector3(float.MaxValue, 0f, float.MaxValue);
    }

    /// <summary>
    /// Called by RoadNetworkBuilder whenever the manager's grass settings change.
    /// Blade size, colour and density are baked into the cell meshes, so any
    /// change means regenerating them; shading is cheap and just re-pushed.
    /// </summary>
    public void ApplySettings(GrassSettings incoming)
    {
        if (incoming != null) settings = incoming.Clone();
        Rebuild();
    }

    private void PushShading(Renderer target)
    {
        if (target == null) return;
        _propertyBlock ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(WindStrengthId, settings.windStrength);
        _propertyBlock.SetFloat(WindSpeedId, settings.windSpeed);
        _propertyBlock.SetFloat(WindScaleId, settings.windScale);
        _propertyBlock.SetFloat(BacklightId, settings.backlight);
        _propertyBlock.SetFloat(SmoothnessId, settings.smoothness);
        target.SetPropertyBlock(_propertyBlock);
    }

    /// <summary>How many grass cells are currently alive around the camera.</summary>
    public int LiveCellCount => _cells.Count;

    /// <summary>Drops every live cell so the next update regenerates them.</summary>
    public void Rebuild()
    {
        _hasPrevCamPos = false;
        ClearCells();
        BuildExclusionIndex();
        _lastBuildPos = new Vector3(float.MaxValue, 0f, float.MaxValue);
    }

    private void Update()
    {
        if (!settings.enabled)
        {
            if (_cells.Count > 0) ClearCells();
            return;
        }
        if (settings.material == null || terrainPolygons == null || terrainPolygons.Count == 0) return;

        Camera cam = ResolveCamera();
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 focus = ComputeFocus(cam, camPos);

        // Only reshuffle cells once the focus has moved a meaningful fraction of a
        // cell; otherwise this would churn every frame while driving.
        if ((focus - _lastBuildPos).sqrMagnitude < (settings.cellSize * 0.25f) * (settings.cellSize * 0.25f)) return;
        _lastBuildPos = focus;

        if (_exclusionIndex == null) BuildExclusionIndex();

        UpdateCells(focus);
    }

    /// <summary>
    /// Where the grass circle should be centred. Offsetting it ahead of the
    /// camera costs nothing - the circle keeps its area, it just sits further
    /// forward - and buys reach where the driver is actually looking.
    /// </summary>
    private Vector3 ComputeFocus(Camera cam, Vector3 camPos)
    {
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 240f, 0.25f);

        // Travel direction while moving, gaze while stopped. A head turn in VR
        // swings gaze far faster than the car can turn, and following it would
        // drag the whole field sideways.
        Vector3 desired = Flatten(cam.transform.forward);
        if (_hasPrevCamPos)
        {
            Vector3 delta = Flatten(camPos - _prevCamPos);
            if (delta.magnitude / dt > settings.biasSpeedThreshold) desired = delta.normalized;
        }
        _prevCamPos = camPos;
        _hasPrevCamPos = true;

        if (desired.sqrMagnitude < 1e-6f) desired = _focusDir;

        // Exponential damp, framerate independent.
        float t = settings.biasSmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-dt / settings.biasSmoothing);
        _focusDir = Vector3.Slerp(_focusDir, desired, t);
        if (_focusDir.sqrMagnitude < 1e-6f) _focusDir = Vector3.forward;
        _focusDir.Normalize();

        return camPos + _focusDir * (settings.forwardBias * settings.viewRadius);
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-8f ? Vector3.zero : v.normalized;
    }

    private Camera ResolveCamera()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var view = UnityEditor.SceneView.lastActiveSceneView;
            return view != null ? view.camera : null;
        }
#endif
        return Camera.main;
    }

    private void UpdateCells(Vector3 focus)
    {
        int radiusCells = Mathf.CeilToInt(settings.viewRadius / settings.cellSize);
        var centre = new Vector2Int(
            Mathf.FloorToInt(focus.x / settings.cellSize),
            Mathf.FloorToInt(focus.z / settings.cellSize));

        float keepSqr = (settings.viewRadius + settings.cellSize) * (settings.viewRadius + settings.cellSize);

        // Retire cells that fell behind.
        _scratchRemove.Clear();
        foreach (var kv in _cells)
        {
            Vector2 cellCentre = new Vector2(
                (kv.Key.x + 0.5f) * settings.cellSize,
                (kv.Key.y + 0.5f) * settings.cellSize);
            float dx = cellCentre.x - focus.x;
            float dz = cellCentre.y - focus.z;
            if (dx * dx + dz * dz > keepSqr) _scratchRemove.Add(kv.Key);
        }
        foreach (var key in _scratchRemove)
        {
            DestroyImmediateSafe(_cells[key]);
            _cells.Remove(key);
        }

        // Bring new cells in range.
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                if (_cells.Count >= settings.maxCells) return;

                var key = new Vector2Int(centre.x + dx, centre.y + dz);
                if (_cells.ContainsKey(key)) continue;

                Vector2 cellCentre = new Vector2((key.x + 0.5f) * settings.cellSize, (key.y + 0.5f) * settings.cellSize);
                float ox = cellCentre.x - focus.x;
                float oz = cellCentre.y - focus.z;
                if (ox * ox + oz * oz > settings.viewRadius * settings.viewRadius) continue;

                GameObject cell = BuildCell(key);
                // Null means the cell was entirely road or off the terrain. Store
                // the null so it is not retried every time the camera nudges.
                _cells[key] = cell;
            }
        }
    }

    private GameObject BuildCell(Vector2Int key)
    {
        float originX = key.x * settings.cellSize;
        float originZ = key.y * settings.cellSize;

        int bladeCount = Mathf.RoundToInt(settings.cellSize * settings.cellSize * settings.density);
        if (bladeCount <= 0) return null;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();

        // Deterministic per-cell stream, so revisiting a cell reproduces it.
        uint seed = Hash((uint)key.x, (uint)key.y);

        for (int i = 0; i < bladeCount; i++)
        {
            float rx = NextFloat(ref seed);
            float rz = NextFloat(ref seed);
            float rHeight = NextFloat(ref seed);
            float rYaw = NextFloat(ref seed);
            float rTint = NextFloat(ref seed);
            float rLean = NextFloat(ref seed);

            var p = new Vector2(originX + rx * settings.cellSize, originZ + rz * settings.cellSize);
            if (!IsGrassAllowed(p)) continue;

            float h = settings.height * (1f - settings.heightVariation + rHeight * settings.heightVariation * 2f);
            float yaw = rYaw * Mathf.PI;
            float tint = 1f - settings.colorJitter * 0.5f + rTint * settings.colorJitter;
            float lean = (rLean - 0.5f) * 0.35f * h;

            AppendBlade(p, h, yaw, tint, lean, verts, normals, uvs, colors, tris);
        }

        if (tris.Count == 0) return null;

        var mesh = new Mesh { name = $"GrassCell_{key.x}_{key.y}" };
        if (verts.Count > 65534) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var go = new GameObject($"GrassCell_{key.x}_{key.y}")
        {
            // Never serialized into the scene; regenerated on demand.
            hideFlags = HideFlags.HideAndDontSave
        };
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = settings.material;
        PushShading(renderer);
        // Thousands of thin blades produce a shadow mess for no visual gain.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return go;
    }

    /// <summary>
    /// One blade as two crossed quads. A single quad disappears edge-on and the
    /// field would flicker as the driver turns; crossing them keeps every blade
    /// visible from any yaw for two extra triangles.
    /// </summary>
    private void AppendBlade(
        Vector2 root,
        float height,
        float yaw,
        float tint,
        float lean,
        List<Vector3> verts,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> tris)
    {
        Color rootColor = settings.baseColor * tint;
        Color tipCol = settings.tipColor * tint;
        rootColor.a = 1f;
        tipCol.a = 1f;

        float y0 = terrainY - settings.rootSink;

        for (int q = 0; q < 2; q++)
        {
            float a = yaw + q * Mathf.PI * 0.5f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var normal = new Vector3(-dir.z, 0f, dir.x);

            Vector3 basePos = new Vector3(root.x, y0, root.y);
            Vector3 tipPos = basePos + Vector3.up * height + dir * lean;

            float halfBase = settings.width * 0.5f;
            float halfTip = settings.width * 0.12f;   // taper to a point

            int b = verts.Count;

            verts.Add(basePos - dir * halfBase);
            verts.Add(basePos + dir * halfBase);
            verts.Add(tipPos - dir * halfTip);
            verts.Add(tipPos + dir * halfTip);

            for (int k = 0; k < 4; k++) normals.Add(normal);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));

            colors.Add(rootColor);
            colors.Add(rootColor);
            colors.Add(tipCol);
            colors.Add(tipCol);

            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Placement tests
    // ─────────────────────────────────────────────────────────────────────

    private bool IsGrassAllowed(Vector2 p)
    {
        bool onTerrain = false;
        for (int i = 0; i < terrainPolygons.Count; i++)
        {
            if (PointInPolygon(p, terrainPolygons[i].points)) { onTerrain = true; break; }
        }
        if (!onTerrain) return false;

        if (_exclusionIndex == null) return true;

        var key = new Vector2Int(
            Mathf.FloorToInt(p.x / _indexCellSize),
            Mathf.FloorToInt(p.y / _indexCellSize));

        if (!_exclusionIndex.TryGetValue(key, out List<int> candidates)) return true;

        for (int i = 0; i < candidates.Count; i++)
        {
            int idx = candidates[i];
            Rect r = _exclusionBounds[idx];
            if (p.x < r.xMin - settings.roadMargin || p.x > r.xMax + settings.roadMargin ||
                p.y < r.yMin - settings.roadMargin || p.y > r.yMax + settings.roadMargin) continue;

            if (PointInPolygonExpanded(p, exclusionPolygons[idx].points, settings.roadMargin)) return false;
        }
        return true;
    }

    /// <summary>
    /// Buckets exclusion polygons into a coarse grid. Without this, every blade
    /// would test every road polygon in the network.
    /// </summary>
    private void BuildExclusionIndex()
    {
        _exclusionIndex = new Dictionary<Vector2Int, List<int>>();
        _indexCellSize = Mathf.Max(settings.cellSize, 8f);

        if (exclusionPolygons == null) return;
        _exclusionBounds = new Rect[exclusionPolygons.Count];

        for (int i = 0; i < exclusionPolygons.Count; i++)
        {
            Vector2[] pts = exclusionPolygons[i]?.points;
            if (pts == null || pts.Length < 3) { _exclusionBounds[i] = new Rect(); continue; }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 pt in pts)
            {
                if (pt.x < minX) minX = pt.x;
                if (pt.x > maxX) maxX = pt.x;
                if (pt.y < minY) minY = pt.y;
                if (pt.y > maxY) maxY = pt.y;
            }
            _exclusionBounds[i] = Rect.MinMaxRect(minX, minY, maxX, maxY);

            int x0 = Mathf.FloorToInt((minX - settings.roadMargin) / _indexCellSize);
            int x1 = Mathf.FloorToInt((maxX + settings.roadMargin) / _indexCellSize);
            int y0 = Mathf.FloorToInt((minY - settings.roadMargin) / _indexCellSize);
            int y1 = Mathf.FloorToInt((maxY + settings.roadMargin) / _indexCellSize);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var key = new Vector2Int(x, y);
                    if (!_exclusionIndex.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        _exclusionIndex[key] = list;
                    }
                    list.Add(i);
                }
            }
        }
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        if (poly == null || poly.Length < 3) return false;
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];
            if ((pi.y > p.y) != (pj.y > p.y) &&
                p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + Mathf.Epsilon) + pi.x)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Inside the polygon, or within <paramref name="margin"/> of an edge.</summary>
    private static bool PointInPolygonExpanded(Vector2 p, Vector2[] poly, float margin)
    {
        if (PointInPolygon(p, poly)) return true;
        if (margin <= 0f || poly == null || poly.Length < 2) return false;

        float marginSqr = margin * margin;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (DistanceToSegmentSqr(p, poly[j], poly[i]) <= marginSqr) return true;
        }
        return false;
    }

    private static float DistanceToSegmentSqr(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 1e-8f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
        return (p - (a + ab * t)).sqrMagnitude;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Hashing
    // ─────────────────────────────────────────────────────────────────────

    private static uint Hash(uint x, uint y)
    {
        uint h = x * 374761393u + y * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>xorshift32, advanced in place. Deterministic per cell.</summary>
    private static float NextFloat(ref uint state)
    {
        if (state == 0u) state = 0x9E3779B9u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFFu) / (float)0x01000000;
    }

    // ─────────────────────────────────────────────────────────────────────

    private void ClearCells()
    {
        foreach (var kv in _cells) DestroyImmediateSafe(kv.Value);
        _cells.Clear();
    }

    private static void DestroyImmediateSafe(GameObject go)
    {
        if (go == null) return;
        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(filter.sharedMesh);
            else DestroyImmediate(filter.sharedMesh);
        }
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
