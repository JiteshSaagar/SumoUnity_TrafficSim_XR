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
                    laneType.Shape);
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

                var laneObj = new GameObject($"LaneSegment_{laneCounter++}");
                laneObj.transform.SetParent(roadNetworkRoot.transform);
                if (groundLayer >= 0) laneObj.layer = groundLayer;
                var mf = laneObj.AddComponent<MeshFilter>();
                var mr = laneObj.AddComponent<MeshRenderer>();
                mf.sharedMesh = laneMesh;
                mr.sharedMaterial = roadSurfaceMaterial ?? GetFallbackMaterial();

                _grassExclusions.Add(new GrassPolygon(BuildLaneFootprint(laneMesh)));

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

            _grassExclusions.Add(new GrassPolygon(BuildLaneFootprint(crossingMesh)));
        }

        SetLayerRecursively(roadNetworkRoot, groundLayer);

        CreateGrassField();
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

    private Material GetFallbackMaterial() => new Material(Shader.Find("Standard"));

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