// ==============================
// RoadNetworkBuilder.cs
// ==============================
#if UNITY_EDITOR
using Assets.Scripts.SUMOImporter.NetFileComponents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEditor;
using UnityEngine;
using NetFile;

public class RoadNetworkBuilder : MonoBehaviour
{
    public static RoadNetworkBuilder Singleton { get; private set; }
    private void Awake() => Singleton = this;
    public void InitializeInEditMode()
    {
        if (Singleton != this)
        {
            Singleton = this;
            Debug.Log("RoadNetworkBuilder: Editor-based initialization complete.");
        }
    }
    private void OnDestroy()
    {
        if (Singleton == this) Singleton = null;
    }

    /// <summary>
    /// Mirrors the manager's grass settings onto the generated field, so tuning
    /// grass never means regenerating the whole road network. Fires on inspector
    /// edits in edit mode and in play mode alike.
    /// </summary>
    private void OnValidate()
    {
        if (grassField == null) grassField = FindOwnedGrassField();
        if (grassField == null) return;

        GrassSettings snapshot = grassSettings;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            // Deferred: OnValidate may not create or destroy objects, and
            // applying settings rebuilds the grass cell GameObjects.
            if (this == null || grassField == null) return;
            grassField.ApplySettings(snapshot);
        };
    }

    [Header("Materials (Road, Junction, Decals)")]
    public Material roadSurfaceMaterial;
    public Material junctionSurfaceMaterial;
    [Tooltip("Legacy URP decal material. Only used as a fallback for zebra crossings; " +
             "lane lines are meshes now and use laneMarkingMaterial instead.")]
    public Material roadMarkingMaterial;

    [Header("Lane Markings (mesh strips)")]
    [Tooltip("Must be an opaque surface material, NOT a URP decal material. " +
             "Leave empty to auto-load Assets/_Project/Materials/Mat_LaneMarking.mat.")]
    public Material laneMarkingMaterial;
    [Tooltip("Painted width of a lane line, in metres.")]
    public float laneMarkingWidth = LaneMarkingMesh.DefaultWidth;
    // Dash length and gap now live on the marking material, because the dash
    // pattern is drawn by the shader rather than cut into the geometry.

    private const string laneMarkingMaterialPath = "Assets/_Project/Materials/Mat_LaneMarking.mat";

    // ★ NEW: Zebra Crossing Support
    [Header("Pedestrian Crossings")]
    public Material zebraCrossingMaterial;

    [Header("Sidewalks & Walking Areas")]
    [Tooltip("Surface material for sidewalks and walking areas. Leave empty to " +
             "auto-load Assets/_Project/Materials/Mat_Sidewalk.mat, falling back " +
             "to the road material.")]
    public Material sidewalkMaterial;

    [Tooltip("Material for the vertical kerb face. Falls back to the sidewalk " +
             "material when empty.")]
    public Material kerbMaterial;

    [Tooltip("How far sidewalks and walking areas sit above the carriageway, in " +
             "metres. 0 renders them flush, as before Phase 4.")]
    [Range(0f, 0.4f)] public float sidewalkHeight = 0.12f;

    [Tooltip("Build the vertical kerb face down to road level. Off gives a " +
             "floating slab, which reads badly at pedestrian eye height.")]
    public bool buildKerbs = true;

    [Tooltip("Metres of world space per pavement texture tile. Sidewalks, " +
             "walking areas and kerbs all use world-space UVs at this density, " +
             "so the pattern never stretches at corners or across junctions. " +
             "Leave the material tiling at 1,1 or the two multiply.")]
    public float sidewalkMetresPerTile = 2f;

    [Tooltip("Lifts walking areas this far above the sidewalks they overlap, to " +
             "break the depth tie that causes z-fighting at junction corners. " +
             "Small enough to be invisible and to not trip a pedestrian raycast.")]
    [Range(0f, 0.02f)] public float walkingAreaDepthBias = 0.004f;

    [Tooltip("Run a pavement band around the exposed rim of each junction. SUMO " +
             "inflates junction shapes to cover the area swept by turning " +
             "vehicles, which at acute corners leaves bare asphalt outside the " +
             "sidewalk; this makes the kerb follow the tarmac outline instead.")]
    public bool buildJunctionPavement = true;

    [Tooltip("Width used for junction pavement bands, in metres. Match it to the " +
             "sidewalk width in the SUMO network (2.0 in Scenario1).")]
    public float sidewalkWidthFallback = 2f;

    [Tooltip("How close a junction outline point must be to a carriageway to " +
             "count as a road mouth and be left unpaved. Too small and a kerb is " +
             "laid across the road; too large and the rim goes bare.")]
    public float junctionPavementMouthClearance = 1.5f;

    [Header("Ground Texturing")]
    [Tooltip("Metres of world space per texture tile on terrain and roadside " +
             "polygons. Smaller = sharper ground. Materials using this should " +
             "have their own tiling left at 1,1.")]
    public float polygonMetresPerTile = 4f;

    [Header("Grass")]
    [Tooltip("Every grass parameter lives here. Edits apply immediately to the " +
             "generated GrassField - no road rebuild needed - and work in play " +
             "mode too.")]
    public GrassSettings grassSettings = new GrassSettings();

    // Kept so live edits can reach the field without hunting for it each time.
    [SerializeField, HideInInspector] private GrassField grassField;

    /// <summary>The grass field this manager drives, for editor tooling.</summary>
    public GrassField GrassFieldRef => grassField;

    /// <summary>
    /// Finds the generated grass field. Deliberately not
    /// GetComponentInChildren: the generated field lives under RoadNetworkRoot,
    /// which is a separate scene root and not a child of this manager, while
    /// GetComponentInChildren *includes this GameObject* and would latch onto a
    /// stray GrassField component sitting on the manager itself. That stray
    /// field has no terrain polygons, so it renders nothing - and once cached,
    /// every settings push would silently go to it instead of the real grass.
    /// </summary>
    private GrassField FindOwnedGrassField()
    {
        if (roadNetworkRoot != null)
        {
            var owned = roadNetworkRoot.GetComponentInChildren<GrassField>(true);
            if (owned != null) return owned;
        }

        foreach (var candidate in FindObjectsByType<GrassField>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Skip a stray component on this manager; prefer a real generated one.
            if (candidate.gameObject == gameObject) continue;
            return candidate;
        }
        return null;
    }

    private const string grassMaterialPath = "Assets/_Project/Materials/Mat_Grass.mat";

    [Header("Polygon Types (Wood/Terrain/Roadside/Residential)")]
    public Material polygonWoodMaterial;
    public Material polygonTerrainMaterial;
    public Material polygonRoadsideMaterial;
    public Material polygonResidentialMaterial;
    private Material polygonFallbackMaterial;

    private GameObject roadNetworkRoot;

    private const string groundLayerName = "Ground";
    private int groundLayer = -1;

    public Dictionary<string, RoadJunctionData> junctionRecords;
    public Dictionary<string, RoadLaneData> laneRecords;
    public Dictionary<string, RoadEdgeData> edgeRecords;
    public Dictionary<string, PolygonShapeData> polygonShapes;

    // ★ NEW: Dictionary for Crossings
    public Dictionary<string, SumoCrossingData> crossingRecords = new Dictionary<string, SumoCrossingData>();

    /// Walking areas: the connective patches at junction corners that join
    /// sidewalks to crossings. Their lane "shape" is a closed outline, not a
    /// centreline, so they are triangulated as polygons rather than extruded.
    public Dictionary<string, List<Vector2>> walkingAreaRecords = new Dictionary<string, List<Vector2>>();

    // Footprints handed to the Social Force Model (Phase 3) as wall boundaries.
    private readonly List<WalkablePolygon> _walkablePolys = new();

    // Pedestrian outlines already built (sidewalk ribbons and walking areas),
    // so junction rim paving only covers rim that is genuinely still bare.
    private readonly List<Vector2[]> _pedestrianFootprints = new();

    // Carriageway outlines, so junction rim paving can tell a road mouth from
    // an exposed edge and avoid laying a kerb across the traffic lanes.
    private readonly List<Vector2[]> _carriagewayFootprints = new();

    private string sumoXmlFolderPath;

    private float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
    private float originX = 0f, originY = 0f;

    private const float laneMeshScaleWidth = 3.2f;
    private const float laneUvVerticalScale = 5f;
    private const float laneUvHorizontalScale = 1f;

    private readonly Dictionary<string, float> laneWidthMap = new();
    private readonly List<Vector2[]> _junctionPolys2D = new();

    // Where grass may grow, and what it must keep off.
    private readonly List<GrassPolygon> _terrainPolys2D = new();
    private readonly List<GrassPolygon> _grassExclusions = new();
    private float _terrainSurfaceY;

    public void LoadSumoXmlFiles(string sumoFilesFolder)
    {
        if (roadNetworkRoot != null)
        {
            DestroyImmediate(roadNetworkRoot);
            roadNetworkRoot = null;
        }
        laneWidthMap.Clear();
        junctionRecords?.Clear();
        laneRecords?.Clear();
        edgeRecords?.Clear();
        polygonShapes?.Clear();
        crossingRecords?.Clear();
        walkingAreaRecords?.Clear();
        _walkablePolys.Clear();
        _carriagewayFootprints.Clear();
        _pedestrianFootprints.Clear();
        _junctionPolys2D.Clear();
        _terrainPolys2D.Clear();
        _grassExclusions.Clear();

        sumoXmlFolderPath = sumoFilesFolder;

        if (groundLayer < 0) groundLayer = LayerMask.NameToLayer(groundLayerName);
        if (groundLayer < 0)
            Debug.LogWarning($"Layer \"{groundLayerName}\" does not exist – objects will keep their current layer.");

        roadNetworkRoot = new GameObject("RoadNetworkRoot");
        if (groundLayer >= 0) roadNetworkRoot.layer = groundLayer;

        var netFilePath = Path.Combine(sumoXmlFolderPath, "Sumo2Unity.net.xml");
        var polyFilePath = Path.Combine(sumoXmlFolderPath, "Sumo2Unity.poly.xml");

        laneRecords = new();
        edgeRecords = new();
        junctionRecords = new();
        polygonShapes = new();
        crossingRecords = new();
        walkingAreaRecords = new();

        NetType netFile;
        {
            var serializer = new XmlSerializer(typeof(NetType));
            using var fs = new FileStream(netFilePath, FileMode.Open, FileAccess.Read);
            using var rd = new StreamReader(fs);
            netFile = (NetType)serializer.Deserialize(rd);
        }

        // ★ NEW: Parse Crossings bypassing XmlSerializer schema
        try
        {
            System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
            xmlDoc.Load(netFilePath);

            // Look specifically for edge tags where function="crossing"
            System.Xml.XmlNodeList crossingEdges = xmlDoc.SelectNodes("//edge[@function='crossing']");
            if (crossingEdges != null)
            {
                int cIndex = 0;
                foreach (System.Xml.XmlNode edgeNode in crossingEdges)
                {
                    System.Xml.XmlNode laneNode = edgeNode.SelectSingleNode("lane");
                    if (laneNode != null)
                    {
                        string cId = edgeNode.Attributes["id"] != null ? edgeNode.Attributes["id"].Value : $"auto_crossing_{cIndex++}";

                        string shape = "";
                        if (laneNode.Attributes["shape"] != null)
                        {
                            shape = laneNode.Attributes["shape"].Value;
                        }

                        float width = laneNode.Attributes["width"] != null ? float.Parse(laneNode.Attributes["width"].Value) : 3.0f;

                        if (!string.IsNullOrEmpty(shape) && !crossingRecords.ContainsKey(cId))
                        {
                            crossingRecords.Add(cId, new SumoCrossingData(cId, "unknown", shape, width));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse custom crossing tags: {ex.Message}");
        }

        // Walking areas, parsed the same way and for the same reason: they are
        // internal edges with no "from" attribute, so the typed pass below skips
        // them entirely. Without this, junction corners have no pedestrian
        // surface and SUMO's pedestrians appear to walk on nothing.
        try
        {
            var waDoc = new System.Xml.XmlDocument();
            waDoc.Load(netFilePath);
            System.Xml.XmlNodeList waEdges = waDoc.SelectNodes("//edge[@function='walkingarea']");
            if (waEdges != null)
            {
                foreach (System.Xml.XmlNode edgeNode in waEdges)
                {
                    var laneNode = edgeNode.SelectSingleNode("lane");
                    if (laneNode?.Attributes?["shape"] == null) continue;

                    string waId = edgeNode.Attributes["id"]?.Value;
                    if (string.IsNullOrEmpty(waId) || walkingAreaRecords.ContainsKey(waId)) continue;

                    var ring = new List<Vector2>();
                    foreach (string pair in laneNode.Attributes["shape"].Value.Split(' '))
                    {
                        if (string.IsNullOrWhiteSpace(pair)) continue;
                        var xy = pair.Split(',');
                        if (xy.Length < 2) continue;
                        ring.Add(new Vector2(
                            float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture)));
                    }

                    if (ring.Count >= 3) walkingAreaRecords.Add(waId, ring);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse walking areas: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(netFile.Location?.ConvBoundary))
        {
            var bounds = netFile.Location.ConvBoundary.Split(',');
            minX = float.Parse(bounds[0]);
            minY = float.Parse(bounds[1]);
            maxX = float.Parse(bounds[2]);
            maxY = float.Parse(bounds[3]);
        }

        if (!string.IsNullOrEmpty(netFile.Location?.NetOffset))
        {
            var p = netFile.Location.NetOffset.Split(',');
            originX = float.Parse(p[0]);
            originY = float.Parse(p[1]);
        }
        else
        {
            originX = minX; originY = minY;
        }

        foreach (JunctionType jt in netFile.Junction)
        {
            if (jt.Type.ToString().Equals("Internal", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(jt.X) || string.IsNullOrEmpty(jt.Y))
            {
                continue;
            }

            try
            {
                var newJunction = new RoadJunctionData(
                    jt.Id,
                    jt.Type,
                    float.Parse(jt.X),
                    float.Parse(jt.Y),
                    string.IsNullOrEmpty(jt.Z) ? 0f : float.Parse(jt.Z),
                    jt.IncLanes,
                    jt.Shape);

                if (!junctionRecords.ContainsKey(newJunction.junctionId))
                    junctionRecords.Add(newJunction.junctionId, newJunction);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Junction {jt.Id} parse error: {ex.Message}");
            }
        }

        foreach (EdgeType et in netFile.Edge)
        {
            if (string.IsNullOrEmpty(et.From)) continue;

            var newEdge = new RoadEdgeData(et.Id, et.From, et.To, et.Priority, et.Shape);
            edgeRecords[et.Id] = newEdge;

            if (et.Lane == null) continue;

            foreach (LaneType laneType in et.Lane)
            {
                float width = laneType.WidthSpecified ? laneType.Width : laneMeshScaleWidth;
                laneWidthMap[laneType.Id] = width;

                newEdge.AddLaneData(
                    laneType.Id,
                    laneType.Index.ToString(),
                    laneType.Speed,
                    laneType.Length,
                    width,
                    laneType.Shape,
                    laneType.Allow,
                    laneType.Disallow);
            }
        }

        if (!File.Exists(polyFilePath)) return;

        try
        {
            var serializer = new XmlSerializer(typeof(AdditionalType));
            using FileStream fs = new FileStream(polyFilePath, FileMode.Open);
            using TextReader rd = new StreamReader(fs);
            AdditionalType additionalPolygons = (AdditionalType)serializer.Deserialize(rd);

            foreach (PolygonType poly in additionalPolygons.Poly)
            {
                if (!IsKnownPolygonType(poly.Type)) continue;

                var shapeData = new PolygonShapeData();
                foreach (string pair in poly.Shape.Split(' '))
                {
                    var parts = pair.Split(',');
                    shapeData.AddPoint(Convert.ToDouble(parts[0]), Convert.ToDouble(parts[1]));
                }
                shapeData.RemoveDuplicateEndPoint();
                if (!polygonShapes.ContainsKey(poly.Id))
                    polygonShapes.Add(poly.Id, shapeData);

                if (shapeData.polygonPoints.Count >= 3)
                    BuildPolygonGameObject(shapeData, poly.Id, poly.Type);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to deserialize polygon file: {e}");
        }

        SetLayerRecursively(roadNetworkRoot, groundLayer);
    }

    public void GenerateRoadsAndJunctions()
    {
        // Junction outlines must exist before the lane pass, which clips lane
        // paint against them. They used to be collected in the junction loop
        // further down, i.e. after the markings had already been placed, so the
        // clip always tested an empty list and paint ran through intersections.
        CollectJunctionPolygons();

        // lanes
        int laneCounter = 0;
        foreach (var edgeData in edgeRecords.Values)
        {
            foreach (var laneData in edgeData.GetLaneDataList())
            {
                var lanePoints = new Vector3[laneData.shapePoints.Count];
                for (int i = 0; i < laneData.shapePoints.Count; i++)
                    lanePoints[i] = ToUnity(laneData.shapePoints[i][0], laneData.shapePoints[i][1]);

                float laneWidth = laneWidthMap.TryGetValue(laneData.laneId, out float w) ? w : laneMeshScaleWidth;

                Mesh laneMesh = CreateLaneMesh(lanePoints, laneWidth, laneUvHorizontalScale, laneUvVerticalScale);
                if (laneMesh == null) continue;

                // Sidewalks are a different surface, not just a differently
                // permitted lane: they sit on a kerb, take the pavement
                // material, and carry no lane markings. They also need mitred
                // corners, which CreateLaneMesh cannot give them.
                if (laneData.IsSidewalk)
                {
                    Mesh walkMesh = CreateSidewalkRibbon(lanePoints, laneWidth, out Vector2[] walkFootprint);
                    if (walkMesh == null) continue;

                    BuildSidewalk(walkMesh, walkFootprint, laneData.laneId);
                    _grassExclusions.Add(new GrassPolygon(walkFootprint));
                    continue;
                }

                Vector2[] footprint = BuildLaneFootprint(laneMesh);
                _carriagewayFootprints.Add(footprint);

                var laneObj = new GameObject($"LaneSegment_{laneCounter++}");
                laneObj.transform.SetParent(roadNetworkRoot.transform);
                if (groundLayer >= 0) laneObj.layer = groundLayer;
                var mf = laneObj.AddComponent<MeshFilter>();
                var mr = laneObj.AddComponent<MeshRenderer>();
                mf.sharedMesh = laneMesh;
                mr.sharedMaterial = roadSurfaceMaterial ?? GetFallbackMaterial();

                _grassExclusions.Add(new GrassPolygon(footprint));

                var ctrl = laneObj.AddComponent<LaneMarkingController>();
                ctrl.markingMaterial = ResolveLaneMarkingMaterial();
                ctrl.markingWidth = laneMarkingWidth;

                // Names stay swapped to match the original decal naming, so the
                // brokenLeft / brokenRight inspector toggles keep their meaning.
                ctrl.rightSpans = LaneMarkingMesh.ClipToOutside(
                    ExtractLeftSideVertices(laneMesh), IsInsideAnyJunction);
                ctrl.leftSpans = LaneMarkingMesh.ClipToOutside(
                    ExtractRightSideVertices(laneMesh), IsInsideAnyJunction);

                ctrl.RebuildAll();
            }
        }

        // junctions
        int junctionCounter = 0;
        foreach (RoadJunctionData j in junctionRecords.Values)
        {
            if (j.shapePoints.Count < 3) continue;

            var verts2D = new Vector2[j.shapePoints.Count];
            for (int i = 0; i < j.shapePoints.Count; i++)
            {
                double[] xy = j.shapePoints[i];
                verts2D[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
            }

            MeshTriangulator triangulator = new MeshTriangulator(verts2D);
            int[] triIndices = triangulator.GenerateIndices();

            var verts3D = new Vector3[verts2D.Length];
            for (int i = 0; i < verts2D.Length; i++)
                verts3D[i] = new Vector3(verts2D[i].x, 0f, verts2D[i].y);

            Mesh junctionMesh = new Mesh
            {
                name = $"Junction_{j.junctionId}",
                vertices = verts3D,
                triangles = triIndices
            };
            junctionMesh.RecalculateNormals();
            junctionMesh.RecalculateBounds();

            Bounds b = junctionMesh.bounds;
            var uvArr = new Vector2[verts3D.Length];
            for (int i = 0; i < verts3D.Length; i++)
                uvArr[i] = new Vector2(
                    (verts3D[i].x - b.min.x) / b.size.x,
                    (verts3D[i].z - b.min.z) / b.size.z);
            junctionMesh.uv = uvArr;

            GameObject jObj = new GameObject($"Junction_{junctionCounter++}");
            jObj.transform.SetParent(roadNetworkRoot.transform);
            if (groundLayer >= 0) jObj.layer = groundLayer;
            var jMf = jObj.AddComponent<MeshFilter>();
            var jMr = jObj.AddComponent<MeshRenderer>();
            jMf.mesh = junctionMesh;
            jMr.material = junctionSurfaceMaterial ?? GetFallbackMaterial();
        }

        // ★ NEW: Build Zebra Crossings (Using Lane Generator)
        int crossingCounter = 0;
        foreach (SumoCrossingData crossing in crossingRecords.Values)
        {
            if (crossing.shapePoints.Count < 2) continue; // Need at least 2 points

            Vector3[] pathPoints = new Vector3[crossing.shapePoints.Count];
            for (int i = 0; i < crossing.shapePoints.Count; i++)
            {
                pathPoints[i] = new Vector3(crossing.shapePoints[i].x - originX, 0f, crossing.shapePoints[i].z - originY);
            }

            Mesh crossingMesh = CreateLaneMesh(pathPoints, crossing.width, 1f, 1f);
            if (crossingMesh == null) continue;

            GameObject crossingObj = new GameObject($"Crossing_{crossingCounter++}_{crossing.crossingId}");
            crossingObj.transform.SetParent(roadNetworkRoot.transform);
            if (groundLayer >= 0) crossingObj.layer = groundLayer;

            crossingObj.transform.localPosition = new Vector3(0f, 0.015f, 0f);

            var cMf = crossingObj.AddComponent<MeshFilter>();
            var cMr = crossingObj.AddComponent<MeshRenderer>();
            cMf.mesh = crossingMesh;

            cMr.material = zebraCrossingMaterial ?? roadMarkingMaterial ?? GetFallbackMaterial();

            Vector2[] crossingFootprint = BuildLaneFootprint(crossingMesh);
            _grassExclusions.Add(new GrassPolygon(crossingFootprint));
            _walkablePolys.Add(new WalkablePolygon(crossingFootprint, WalkableKind.Crossing));
        }

        BuildWalkingAreas();
        BuildJunctionPavements();
        PublishWalkableAreas();

        SetLayerRecursively(roadNetworkRoot, groundLayer);

        CreateGrassField();
    }

    /// <summary>
    /// Builds one sidewalk: the raised walking surface plus, optionally, the
    /// vertical kerb face down to carriageway level.
    ///
    /// The slab is lifted by moving the GameObject rather than the vertices, so
    /// the mesh stays identical to a carriageway lane and the footprint used for
    /// grass exclusion and social-force walls needs no separate transform.
    /// </summary>
    private void BuildSidewalk(Mesh laneMesh, Vector2[] footprint, string laneId)
    {
        var obj = new GameObject($"Sidewalk_{laneId}");
        obj.transform.SetParent(roadNetworkRoot.transform);
        obj.transform.localPosition = new Vector3(0f, sidewalkHeight, 0f);
        if (groundLayer >= 0) obj.layer = groundLayer;

        var mf = obj.AddComponent<MeshFilter>();
        var mr = obj.AddComponent<MeshRenderer>();
        mf.sharedMesh = laneMesh;
        mr.sharedMaterial = RequireMaterial(ResolveSidewalkMaterial(), "sidewalk");

        // A collider so the desktop and XR pedestrians can stand on the kerb
        // rather than clip through it. DesktopPedestrianController raycasts down
        // and keeps its last height when nothing is hit, so before Phase 4 it
        // simply walked at road level everywhere.
        obj.AddComponent<MeshCollider>().sharedMesh = laneMesh;

        if (buildKerbs && sidewalkHeight > 0.001f)
        {
            Mesh kerb = BuildKerbMesh(footprint, sidewalkHeight);
            if (kerb != null)
            {
                var kerbObj = new GameObject($"Kerb_{laneId}");
                kerbObj.transform.SetParent(roadNetworkRoot.transform);
                if (groundLayer >= 0) kerbObj.layer = groundLayer;
                kerbObj.AddComponent<MeshFilter>().sharedMesh = kerb;
                // Deliberately not `kerbMaterial ?? ResolveSidewalkMaterial()`:
                // ?? bypasses Unity's overloaded == operator, so an unassigned
                // or destroyed Material sneaks through as non-null and the kerb
                // renders magenta.
                kerbObj.AddComponent<MeshRenderer>().sharedMaterial = RequireMaterial(
                    kerbMaterial != null ? kerbMaterial : ResolveSidewalkMaterial(), "kerb");
            }
        }

        _walkablePolys.Add(new WalkablePolygon(footprint, WalkableKind.Sidewalk));
        _pedestrianFootprints.Add(footprint);
    }

    /// <summary>
    /// Builds a sidewalk ribbon with mitred corners and world-space UVs.
    ///
    /// Deliberately separate from <see cref="CreateLaneMesh"/>, which emits four
    /// unshared vertices per segment and recomputes the perpendicular from each
    /// segment in isolation. On a straight that is fine; at a corner the two
    /// quads meeting at a point use different perpendiculars, so they splay
    /// apart on the outside of the turn and overlap on the inside. A 2 m
    /// sidewalk with a tiled pattern makes that obvious, where dark asphalt hid
    /// it. CreateLaneMesh is left untouched because LaneMarkingController reads
    /// its exact vertex layout through Extract*SideVertices.
    ///
    /// Here each centre point produces exactly one left and one right vertex,
    /// offset along the angle bisector, so consecutive segments share an edge
    /// and the ribbon stays continuous through corners.
    /// </summary>
    private Mesh CreateSidewalkRibbon(Vector3[] pts, float width, out Vector2[] footprint)
    {
        footprint = null;
        if (pts == null || pts.Length < 2) return null;

        // Drop repeated points: a zero-length segment has no direction, and the
        // bisector at that point would be undefined.
        var pl = new List<Vector3>(pts.Length) { pts[0] };
        for (int i = 1; i < pts.Length; i++)
        {
            if ((pts[i] - pl[pl.Count - 1]).sqrMagnitude > 1e-6f) pl.Add(pts[i]);
        }
        if (pl.Count < 2) return null;

        int n = pl.Count;
        float half = width * 0.5f;

        var left = new Vector3[n];
        var right = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Vector3 dPrev = i > 0 ? (pl[i] - pl[i - 1]).normalized : Vector3.zero;
            Vector3 dNext = i < n - 1 ? (pl[i + 1] - pl[i]).normalized : Vector3.zero;

            if (i == 0) dPrev = dNext;
            if (i == n - 1) dNext = dPrev;

            Vector3 nPrev = new Vector3(-dPrev.z, 0f, dPrev.x);
            Vector3 nNext = new Vector3(-dNext.z, 0f, dNext.x);

            Vector3 miter = nPrev + nNext;
            if (miter.sqrMagnitude < 1e-8f)
            {
                // A near-180 degree reversal: the bisector collapses. Fall back
                // to the incoming normal rather than emitting a NaN vertex.
                miter = nNext;
            }
            miter.Normalize();

            // 1/cos(theta/2) lengthens the offset so the outer edge stays
            // parallel to both segments. Clamped, because a hairpin would
            // otherwise throw a spike halfway across the map.
            float cos = Vector3.Dot(miter, nNext);
            float scale = Mathf.Abs(cos) < 0.25f ? 4f : 1f / cos;
            scale = Mathf.Clamp(scale, -4f, 4f);

            Vector3 offset = miter * half * scale;
            left[i] = pl[i] + offset;
            right[i] = pl[i] - offset;
        }

        int segs = n - 1;
        var vertices = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        var triangles = new int[segs * 6];

        float tile = Mathf.Max(0.05f, sidewalkMetresPerTile);
        for (int i = 0; i < n; i++)
        {
            vertices[i * 2 + 0] = left[i];
            vertices[i * 2 + 1] = right[i];

            // World-space UVs: constant texel density everywhere, so the pattern
            // neither stretches on long straights nor bunches at corners, and it
            // matches the walking areas and kerbs that abut it.
            uvs[i * 2 + 0] = new Vector2(left[i].x / tile, left[i].z / tile);
            uvs[i * 2 + 1] = new Vector2(right[i].x / tile, right[i].z / tile);
        }

        for (int i = 0; i < segs; i++)
        {
            int v = i * 2;
            int t = i * 6;
            triangles[t + 0] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 3;
            triangles[t + 5] = v + 1;
        }

        var mesh = new Mesh
        {
            name = "SidewalkRibbon",
            vertices = vertices,
            uv = uvs,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
        {
            FlipTriangleWinding(mesh);
            mesh.RecalculateNormals();
        }

        // Closed outline: down the left edge, back along the right.
        footprint = new Vector2[n * 2];
        for (int i = 0; i < n; i++) footprint[i] = new Vector2(left[i].x, left[i].z);
        for (int i = 0; i < n; i++)
            footprint[n + i] = new Vector2(right[n - 1 - i].x, right[n - 1 - i].z);

        return mesh;
    }

    /// <summary>
    /// World-space UVs for an already-built polygon mesh, so a walking area
    /// tiles at the same density as the sidewalks it joins. Bounds-normalised
    /// UVs stretch one whole tile across the polygon regardless of its size,
    /// which is why small landing areas looked smeared next to the pavement.
    /// </summary>
    private void ApplyWorldSpaceUv(Mesh mesh)
    {
        Vector3[] v = mesh.vertices;
        var uv = new Vector2[v.Length];
        float tile = Mathf.Max(0.05f, sidewalkMetresPerTile);
        for (int i = 0; i < v.Length; i++)
            uv[i] = new Vector2(v[i].x / tile, v[i].z / tile);
        mesh.uv = uv;
    }

    /// <summary>
    /// A vertical skirt around a footprint, from <paramref name="height"/> down
    /// to 0. Built as a separate mesh from the slab so the kerb face can take a
    /// different material without needing submeshes.
    /// </summary>
    private Mesh BuildKerbMesh(Vector2[] footprint, float height)
    {
        if (footprint == null || footprint.Length < 2) return null;

        int n = footprint.Length;
        var verts = new Vector3[n * 4];
        var uvs = new Vector2[n * 4];
        var tris = new int[n * 6];

        float running = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = footprint[i];
            Vector2 b = footprint[(i + 1) % n];

            int v = i * 4;
            verts[v + 0] = new Vector3(a.x, height, a.y);
            verts[v + 1] = new Vector3(b.x, height, b.y);
            verts[v + 2] = new Vector3(a.x, 0f, a.y);
            verts[v + 3] = new Vector3(b.x, 0f, b.y);

            // Horizontal UV follows distance along the kerb and vertical
            // follows real height, both divided by the same tile size the
            // pavement uses, so kerb and slab share one texel density.
            float seg = Vector2.Distance(a, b);
            float tile = Mathf.Max(0.05f, sidewalkMetresPerTile);
            float u0 = running / tile;
            float u1 = (running + seg) / tile;
            float vTop = height / tile;
            uvs[v + 0] = new Vector2(u0, vTop);
            uvs[v + 1] = new Vector2(u1, vTop);
            uvs[v + 2] = new Vector2(u0, 0f);
            uvs[v + 3] = new Vector2(u1, 0f);
            running += seg;

            int t = i * 6;
            tris[t + 0] = v + 0; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "Kerb", vertices = verts, uv = uvs, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Builds the walking areas: the connective patches at junction corners that
    /// join sidewalks to crossings. Unlike a lane, a walking area SUMO shape is
    /// already a closed outline, so it is triangulated directly rather than
    /// extruded along a centreline.
    /// </summary>
    private void BuildWalkingAreas()
    {
        if (walkingAreaRecords == null || walkingAreaRecords.Count == 0) return;

        int built = 0, skipped = 0;
        foreach (var kv in walkingAreaRecords)
        {
            List<Vector2> ring = DedupeRing(kv.Value);
            if (ring.Count < 3) continue;

            var verts2D = new Vector2[ring.Count];
            for (int i = 0; i < ring.Count; i++)
                verts2D[i] = new Vector2(ring[i].x - originX, ring[i].y - originY);

            int[] tris;
            try
            {
                tris = new MeshTriangulator(verts2D).GenerateIndices();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Walking area {kv.Key} could not be triangulated: {ex.Message}");
                continue;
            }
            if (tris == null || tris.Length < 3)
            {
                Debug.LogWarning($"Walking area {kv.Key} produced no triangles " +
                                 $"({ring.Count} points); skipped.");
                skipped++;
                continue;
            }

            var verts3D = new Vector3[verts2D.Length];
            for (int i = 0; i < verts2D.Length; i++)
                verts3D[i] = new Vector3(verts2D[i].x, 0f, verts2D[i].y);

            var mesh = new Mesh { name = $"WalkingArea_{kv.Key}", vertices = verts3D, triangles = tris };
            mesh.RecalculateNormals();

            // Same guard BuildPolygonGameObject uses. Measured on Scenario1 the
            // walking areas and the junctions share a winding, so this is not
            // what was hiding them - but a downward-facing polygon is invisible
            // and costs nothing to rule out.
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                FlipTriangleWinding(mesh);
                mesh.RecalculateNormals();
            }

            Bounds b = mesh.bounds;
            var uv = new Vector2[verts3D.Length];
            for (int i = 0; i < verts3D.Length; i++)
                uv[i] = new Vector2(
                    b.size.x > 0f ? (verts3D[i].x - b.min.x) / b.size.x : 0f,
                    b.size.z > 0f ? (verts3D[i].z - b.min.z) / b.size.z : 0f);
            mesh.uv = uv;
            ApplyWorldSpaceUv(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var obj = new GameObject($"WalkingArea_{kv.Key}");
            obj.transform.SetParent(roadNetworkRoot.transform);
            // Sits level with the sidewalks it connects, otherwise pedestrians
            // would step down and back up at every junction corner.
            //
            // The extra few millimetres are not cosmetic padding: measured on
            // Scenario1, 23 sidewalk/walking-area pairs genuinely overlap at
            // junction corners, because SUMO cuts the sidewalk back to the
            // junction while the walking area reaches out to meet it. Two
            // coplanar surfaces at exactly the same height give the depth test
            // nothing to separate them, and the result is the shimmering
            // patchwork that shows up around sharp junctions. Breaking the tie
            // is the standard fix - the same trick the crossings already use to
            // sit above the carriageway.
            obj.transform.localPosition =
                new Vector3(0f, sidewalkHeight + walkingAreaDepthBias, 0f);
            if (groundLayer >= 0) obj.layer = groundLayer;

            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial =
                RequireMaterial(ResolveSidewalkMaterial(), "walking area");
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;

            // A walking area is raised to kerb height like the sidewalks it
            // joins, so without its own skirt it reads as a floating slab -
            // most visible right at the crossing, where you look straight at
            // the open edge.
            if (buildKerbs && sidewalkHeight > 0.001f)
            {
                Mesh waKerb = BuildKerbMesh(verts2D, sidewalkHeight);
                if (waKerb != null)
                {
                    var waKerbObj = new GameObject($"Kerb_{kv.Key}");
                    waKerbObj.transform.SetParent(roadNetworkRoot.transform);
                    if (groundLayer >= 0) waKerbObj.layer = groundLayer;
                    waKerbObj.AddComponent<MeshFilter>().sharedMesh = waKerb;
                    waKerbObj.AddComponent<MeshRenderer>().sharedMaterial = RequireMaterial(
                        kerbMaterial != null ? kerbMaterial : ResolveSidewalkMaterial(), "kerb");
                }
            }

            _grassExclusions.Add(new GrassPolygon((Vector2[])verts2D.Clone()));
            _walkablePolys.Add(new WalkablePolygon((Vector2[])verts2D.Clone(), WalkableKind.WalkingArea));
            _pedestrianFootprints.Add((Vector2[])verts2D.Clone());
            built++;
        }

        Debug.Log($"Built {built} walking area(s)" +
                  (skipped > 0 ? $", skipped {skipped}." : "."));
    }

    /// <summary>
    /// Removes duplicate points, including a first vertex repeated at the end.
    ///
    /// This is what broke the walking areas either side of the J8 zebra crossing.
    /// SUMO closes *some* walking-area outlines explicitly - measured: 2 of the
    /// 20 in Scenario1, and both of them are J8's - while junction outlines are
    /// never closed, which is why junctions triangulated fine and these did not.
    ///
    /// The repeated vertex leaves a zero-length edge that the ear clipper can
    /// never snip. It does not fail outright: simulating the triangulator on
    /// J8's rings gives **3 indices instead of 6**, so the 4x2 m landing area
    /// rendered as a single triangle and half of it was simply missing - which
    /// reads on screen as a gap in the pavement at the kerb. After deduping,
    /// both rings produce the full 6 indices.
    /// </summary>
    private static List<Vector2> DedupeRing(List<Vector2> ring)
    {
        var outRing = new List<Vector2>(ring.Count);
        const float epsSqr = 1e-6f;

        foreach (Vector2 p in ring)
        {
            if (outRing.Count == 0 || (outRing[outRing.Count - 1] - p).sqrMagnitude > epsSqr)
                outRing.Add(p);
        }

        // Closing point repeated at the end.
        while (outRing.Count > 1 && (outRing[0] - outRing[outRing.Count - 1]).sqrMagnitude <= epsSqr)
            outRing.RemoveAt(outRing.Count - 1);

        return outRing;
    }

    /// <summary>
    /// Runs a pavement band around the exposed rim of each junction, so the
    /// kerb line follows the asphalt outline instead of cutting across it.
    ///
    /// SUMO inflates a junction shape to cover the area swept by turning
    /// vehicles. At acute corners that produces a blob far larger than the roads
    /// meeting there - measured on Scenario1, the junction outline overshoots the
    /// nearest pedestrian surface by up to 7.75 m at J0, and by about 4 m at J3
    /// and J4, while the other five junctions are already flush. Rendering that
    /// whole blob as asphalt leaves bare road outside the sidewalk, with the
    /// pavement curving on a visibly different radius from the tarmac.
    ///
    /// The band is generated by walking the junction outline inwards by half the
    /// sidewalk width, so the ribbon's outer edge lands exactly on the outline -
    /// same curve, same radius. Stretches of outline that are a road mouth are
    /// skipped, otherwise the band would lay a kerb across the carriageway.
    /// </summary>
    private void BuildJunctionPavements()
    {
        if (!buildJunctionPavement || junctionRecords == null) return;

        int built = 0;
        foreach (RoadJunctionData j in junctionRecords.Values)
        {
            if (j.shapePoints.Count < 3) continue;

            int n = j.shapePoints.Count;
            var outline = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                double[] xy = j.shapePoints[i];
                outline[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
            }

            // Which vertices sit on a road mouth: those are where traffic enters,
            // and a raised band there would be a kerb across the road.
            // Exposed rim only. Measured on Scenario1, just J0, J3 and J4
            // overshoot their pedestrian surfaces (by 7.75 m, 4.07 m and 4.00 m);
            // the other five junctions are already flush, and paving those would
            // lay a kerb across the carriageway. A vertex is skipped when it is
            // a road mouth or when a sidewalk or walking area already covers it.
            var isMouth = new bool[n];
            for (int i = 0; i < n; i++)
            {
                // The paved test uses the full band width, not the mouth
                // clearance: the band reaches sidewalkWidthFallback inwards from
                // the outline, so anything nearer than that would be overlapped
                // and the two coplanar surfaces would z-fight.
                isMouth[i] = IsNearCarriageway(outline[i], junctionPavementMouthClearance)
                             || IsAlreadyPaved(outline[i], sidewalkWidthFallback);
            }

            // Nothing exposed, or nothing but mouth - either way there is no rim
            // to pave.
            bool anyFree = false, anyMouth = false;
            for (int i = 0; i < n; i++) { anyFree |= !isMouth[i]; anyMouth |= isMouth[i]; }
            if (!anyFree) continue;

            Vector2 centre = Vector2.zero;
            for (int i = 0; i < n; i++) centre += outline[i];
            centre /= n;

            float halfW = sidewalkWidthFallback * 0.5f;

            // Contiguous runs of exposed outline. When the whole ring is exposed
            // the run wraps, so start from a mouth vertex where one exists.
            int start = 0;
            if (anyMouth) { while (start < n && !isMouth[start]) start++; }

            var run = new List<Vector3>();
            for (int k = 0; k <= n; k++)
            {
                int i = (start + k) % n;
                bool free = !isMouth[i] && (anyMouth || k < n);

                if (free)
                {
                    // Step inwards so the ribbon's OUTER edge lands on the
                    // outline itself - that is what makes the radii agree.
                    Vector2 inward = (centre - outline[i]).normalized;
                    Vector2 p = outline[i] + inward * halfW;
                    run.Add(new Vector3(p.x, 0f, p.y));
                }

                if ((!free || k == n) && run.Count >= 2)
                {
                    Mesh band = CreateSidewalkRibbon(run.ToArray(), sidewalkWidthFallback,
                                                     out Vector2[] bandFootprint);
                    if (band != null)
                    {
                        BuildSidewalk(band, bandFootprint, $"{j.junctionId}_rim{built}");
                        _grassExclusions.Add(new GrassPolygon(bandFootprint));
                        built++;
                    }
                    run.Clear();
                }
                else if (!free)
                {
                    run.Clear();
                }
            }
        }

        if (built > 0) Debug.Log($"Built {built} junction pavement band(s).");
    }

    /// <summary>
    /// True if a sidewalk or walking area already covers this point, so the rim
    /// paving does not double up on pavement that exists.
    /// </summary>
    private bool IsAlreadyPaved(Vector2 p, float clearance)
    {
        float c2 = clearance * clearance;
        for (int i = 0; i < _pedestrianFootprints.Count; i++)
        {
            Vector2[] poly = _pedestrianFootprints[i];
            if (poly == null || poly.Length < 3) continue;
            if (PointInPolygon(p, poly)) return true;
            for (int a = 0; a < poly.Length; a++)
            {
                if (SqrDistanceToSegment(p, poly[a], poly[(a + 1) % poly.Length]) <= c2) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True if the point lies on, or within <paramref name="clearance"/> of, a
    /// carriageway lane. Used to leave road mouths unpaved.
    /// </summary>
    private bool IsNearCarriageway(Vector2 p, float clearance)
    {
        float c2 = clearance * clearance;
        for (int i = 0; i < _carriagewayFootprints.Count; i++)
        {
            Vector2[] poly = _carriagewayFootprints[i];
            if (poly == null || poly.Length < 3) continue;
            if (PointInPolygon(p, poly)) return true;

            for (int a = 0; a < poly.Length; a++)
            {
                Vector2 s0 = poly[a];
                Vector2 s1 = poly[(a + 1) % poly.Length];
                if (SqrDistanceToSegment(p, s0, s1) <= c2) return true;
            }
        }
        return false;
    }

    private static float SqrDistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-9f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return (p - (a + ab * t)).sqrMagnitude;
    }

    /// <summary>
    /// Publishes every walkable footprint onto the generated root, where the
    /// Social Force Model reads them as wall boundaries (Phase 3).
    /// </summary>
    private void PublishWalkableAreas()
    {
        var registry = roadNetworkRoot.GetComponent<WalkableAreas>()
                       ?? roadNetworkRoot.AddComponent<WalkableAreas>();
        registry.polygons = new List<WalkablePolygon>(_walkablePolys);
        registry.surfaceHeight = sidewalkHeight;
        Debug.Log($"WalkableAreas: {registry.Summary()}.");
    }

    /// <summary>
    /// Guards against handing a MeshRenderer a null or shaderless material,
    /// which Unity draws as flat magenta with nothing in the console to say why.
    /// </summary>
    private Material RequireMaterial(Material candidate, string usage)
    {
        if (candidate != null && candidate.shader != null) return candidate;

        Debug.LogError(
            $"No usable {usage} material (material null: {candidate == null}). " +
            "Run 'Sumo2Unity > 5. Create Sidewalk Material', or assign one on the " +
            "Road Network Builder, then rebuild. Falling back to the road material.");

        return roadSurfaceMaterial != null ? roadSurfaceMaterial : GetFallbackMaterial();
    }

    private Material ResolveSidewalkMaterial()
    {
        if (sidewalkMaterial != null) return sidewalkMaterial;

        const string path = "Assets/_Project/Materials/Mat_Sidewalk.mat";
        var found = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (found != null)
        {
            sidewalkMaterial = found;
            return found;
        }

        // Falling back to the road material keeps the build working; it just
        // looks like the pre-Phase-4 flat asphalt.
        return roadSurfaceMaterial ?? GetFallbackMaterial();
    }

    /// <summary>
    /// Caches every non-internal junction outline in Unity XZ space, for the
    /// point-in-polygon test that keeps lane paint out of intersections.
    /// </summary>
    private void CollectJunctionPolygons()
    {
        _junctionPolys2D.Clear();
        if (junctionRecords == null) return;

        foreach (RoadJunctionData j in junctionRecords.Values)
        {
            if (j.shapePoints.Count < 3) continue;

            var poly = new Vector2[j.shapePoints.Count];
            for (int i = 0; i < j.shapePoints.Count; i++)
            {
                double[] xy = j.shapePoints[i];
                poly[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
            }
            _junctionPolys2D.Add(poly);
        }
    }

    /// <summary>
    /// Lane paint renders through a normal MeshRenderer, so it needs an opaque
    /// surface material. roadMarkingMaterial is a URP *decal* material and
    /// would draw nothing here, hence the separate slot and asset fallback.
    /// </summary>
    private Material ResolveLaneMarkingMaterial()
    {
        if (laneMarkingMaterial != null) return laneMarkingMaterial;

        laneMarkingMaterial = AssetDatabase.LoadAssetAtPath<Material>(laneMarkingMaterialPath);
        if (laneMarkingMaterial != null) return laneMarkingMaterial;

        Shader shader = Shader.Find("Sumo2Unity/Road Marking");
        if (shader != null)
        {
            laneMarkingMaterial = new Material(shader) { name = "LaneMarking (runtime)" };
            return laneMarkingMaterial;
        }

        Debug.LogWarning(
            $"Lane marking material not found at {laneMarkingMaterialPath} and shader " +
            "\"Sumo2Unity/Road Marking\" is missing. Falling back to Standard; " +
            "expect Z-fighting against the road surface.");
        return GetFallbackMaterial();
    }

    /// <summary>
    /// Closed outline of a lane quad strip: the left boundary out, the right
    /// boundary back. Used to keep grass off the road.
    /// </summary>
    private Vector2[] BuildLaneFootprint(Mesh laneMesh)
    {
        Vector3[] left = ExtractLeftSideVertices(laneMesh);
        Vector3[] right = ExtractRightSideVertices(laneMesh);

        var ring = new Vector2[left.Length + right.Length];
        for (int i = 0; i < left.Length; i++)
            ring[i] = new Vector2(left[i].x, left[i].z);
        for (int i = 0; i < right.Length; i++)
            ring[left.Length + i] = new Vector2(right[right.Length - 1 - i].x, right[right.Length - 1 - i].z);

        return ring;
    }

    /// <summary>
    /// Attaches the grass field. Blades are generated around the camera at
    /// runtime rather than baked, because Scenario2's terrain alone is roughly
    /// 813,000 m2 and would be tens of millions of blades.
    /// </summary>
    private void CreateGrassField()
    {
        if (!grassSettings.enabled || _terrainPolys2D.Count == 0) return;

        var go = new GameObject("GrassField");
        go.transform.SetParent(roadNetworkRoot.transform, false);

        var field = go.AddComponent<GrassField>();
        grassSettings.material = ResolveGrassMaterial();
        field.settings = grassSettings.Clone();
        field.terrainY = _terrainSurfaceY;
        grassField = field;
        field.terrainPolygons = new List<GrassPolygon>(_terrainPolys2D);

        var exclusions = new List<GrassPolygon>(_grassExclusions);
        foreach (Vector2[] poly in _junctionPolys2D)
            exclusions.Add(new GrassPolygon((Vector2[])poly.Clone()));
        field.exclusionPolygons = exclusions;

        field.Rebuild();

        Debug.Log($"GrassField: {field.terrainPolygons.Count} terrain polygon(s), " +
                  $"{exclusions.Count} exclusion polygon(s).");
    }

    private Material ResolveGrassMaterial()
    {
        if (grassSettings.material != null) return grassSettings.material;

        Material found = AssetDatabase.LoadAssetAtPath<Material>(grassMaterialPath);
        if (found != null) return found;

        Shader shader = Shader.Find("Sumo2Unity/Grass");
        if (shader != null) return new Material(shader) { name = "Grass (runtime)" };

        Debug.LogWarning($"Grass material not found at {grassMaterialPath} and shader " +
                         "\"Sumo2Unity/Grass\" is missing; grass will not render.");
        return null;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer < 0) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private Vector3 ToUnity(double x, double y) => new((float)(x - originX), 0f, (float)(y - originY));

    private void BuildPolygonGameObject(PolygonShapeData shapeData, string polygonId, string polygonType)
    {
        var vertices2D = new Vector2[shapeData.polygonPoints.Count];
        for (int i = 0; i < shapeData.polygonPoints.Count; i++)
        {
            double[] xy = shapeData.polygonPoints[i];
            vertices2D[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
        }

        MeshTriangulator tri = new MeshTriangulator(vertices2D);
        int[] indices = tri.GenerateIndices();

        var vertices3D = new Vector3[vertices2D.Length];
        for (int i = 0; i < vertices2D.Length; i++)
            vertices3D[i] = new Vector3(vertices2D[i].x, 0f, vertices2D[i].y);

        Mesh polyMesh = new Mesh
        {
            name = $"Polygon_{polygonId}",
            vertices = vertices3D,
            triangles = indices
        };
        polyMesh.RecalculateNormals();

        if (polyMesh.normals.Length > 0 && polyMesh.normals[0].y < 0f)
        {
            FlipTriangleWinding(polyMesh);
            polyMesh.RecalculateNormals();
        }
        polyMesh.RecalculateBounds();

        // World-space UVs in tile units. Normalising 0..1 over the polygon
        // bounds stretched one texture tile across the whole terrain: 15.7 m per
        // tile in Scenario1 and 87 m in Scenario2, which is why the ground read
        // as blurry and smeared. Metres-per-tile keeps texel density constant
        // no matter how large the polygon is.
        float tile = Mathf.Max(polygonMetresPerTile, 0.01f);
        var uvs = new Vector2[vertices3D.Length];
        for (int i = 0; i < vertices3D.Length; i++)
            uvs[i] = new Vector2(vertices3D[i].x / tile, vertices3D[i].z / tile);
        polyMesh.uv = uvs;

        GameObject polyGO = new GameObject($"Shape_{polygonId}");
        polyGO.transform.SetParent(roadNetworkRoot.transform);
        if (groundLayer >= 0) polyGO.layer = groundLayer;

        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("terrain"))
        {
            polyGO.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            _terrainSurfaceY = -0.02f;
            _terrainPolys2D.Add(new GrassPolygon((Vector2[])vertices2D.Clone()));
        }
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("roadside"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("wood"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("residential"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);

        var mf = polyGO.AddComponent<MeshFilter>();
        var mr = polyGO.AddComponent<MeshRenderer>();
        mf.sharedMesh = polyMesh;
        mr.sharedMaterial = GetPolygonMaterial(polygonType);

        if (!string.IsNullOrEmpty(polygonType) && (polygonType.Equals("terrain", StringComparison.OrdinalIgnoreCase)
            || polygonType.ToLowerInvariant().Contains("terrain")))
        {
            var meshCol = polyGO.AddComponent<MeshCollider>();
            meshCol.sharedMesh = polyMesh;
            meshCol.convex = false;
        }
    }

    private void FlipTriangleWinding(Mesh mesh)
    {
        int[] tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
            (tris[i], tris[i + 2]) = (tris[i + 2], tris[i]);
        mesh.triangles = tris;
    }

    private Material GetPolygonMaterial(string type)
    {
        string t = (type ?? string.Empty).ToLowerInvariant();
        if (t.Contains("wood") && polygonWoodMaterial != null) return polygonWoodMaterial;
        if (t.Contains("terrain") && polygonTerrainMaterial != null) return polygonTerrainMaterial;
        if (t.Contains("roadside") && polygonRoadsideMaterial != null) return polygonRoadsideMaterial;
        if (t.Contains("residential") && polygonResidentialMaterial != null) return polygonResidentialMaterial;
        return polygonFallbackMaterial ?? GetFallbackMaterial();
    }

    private bool IsKnownPolygonType(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        var l = t.ToLowerInvariant();
        return l.Contains("wood") || l.Contains("terrain") || l.Contains("roadside") || l.Contains("residential");
    }

    /// <summary>
    /// Last-resort material. "Standard" does not exist in a URP project -
    /// Shader.Find returns null and `new Material(null)` renders magenta - so
    /// the URP shader is tried first.
    /// </summary>
    private Material GetFallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("No usable fallback shader found; meshes will render magenta.");
            return null;
        }
        return new Material(shader);
    }

    private Mesh CreateLaneMesh(Vector3[] lanePoints, float roadWidth, float uvScaleU, float uvScaleV)
    {
        if (lanePoints.Length < 2) return null;

        int segmentCount = lanePoints.Length - 1;
        var vertices = new Vector3[segmentCount * 4];
        var uvs = new Vector2[segmentCount * 4];
        var triangles = new int[segmentCount * 6];

        float accumulatedDist = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = lanePoints[i];
            Vector3 p1 = lanePoints[i + 1];
            Vector3 dir = (p1 - p0).normalized;
            float segLen = Vector3.Distance(p0, p1);
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * (roadWidth * 0.5f);

            Vector3 leftA = p0 + perp;
            Vector3 rightA = p0 - perp;
            Vector3 leftB = p1 + perp;
            Vector3 rightB = p1 - perp;

            int v = i * 4;
            vertices[v] = leftA;
            vertices[v + 1] = rightA;
            vertices[v + 2] = leftB;
            vertices[v + 3] = rightB;

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 3;
            triangles[t + 5] = v + 1;

            float v0 = accumulatedDist / uvScaleV;
            float v1 = (accumulatedDist + segLen) / uvScaleV;
            float u0 = 0f;
            float u1 = roadWidth / uvScaleU;

            uvs[v] = new Vector2(u0, v0);
            uvs[v + 1] = new Vector2(u1, v0);
            uvs[v + 2] = new Vector2(u0, v1);
            uvs[v + 3] = new Vector2(u1, v1);

            accumulatedDist += segLen;
        }

        Mesh mesh = new Mesh
        {
            name = "LaneMesh",
            vertices = vertices,
            uv = uvs,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3[] ExtractLeftSideVertices(Mesh laneMesh)
    {
        Vector3[] v = laneMesh.vertices;
        if (v.Length < 4) return Array.Empty<Vector3>();
        int segCount = v.Length / 4;
        var pts = new Vector3[segCount + 1];
        for (int i = 0; i < segCount; i++) pts[i] = v[i * 4];
        pts[segCount] = v[(segCount - 1) * 4 + 2];
        return pts;
    }

    private Vector3[] ExtractRightSideVertices(Mesh laneMesh)
    {
        Vector3[] v = laneMesh.vertices;
        if (v.Length < 4) return Array.Empty<Vector3>();
        int segCount = v.Length / 4;
        var pts = new Vector3[segCount + 1];
        for (int i = 0; i < segCount; i++) pts[i] = v[i * 4 + 1];
        pts[segCount] = v[(segCount - 1) * 4 + 3];
        return pts;
    }


    private bool IsInsideAnyJunction(Vector3 worldPos)
    {
        Vector2 p = new Vector2(worldPos.x, worldPos.z);
        foreach (var poly in _junctionPolys2D)
        {
            if (PointInPolygon(p, poly)) return true;
        }
        return false;
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];
            bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                             (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + Mathf.Epsilon) + pi.x);
            if (intersect) inside = !inside;
        }
        return inside;
    }
}
#endif