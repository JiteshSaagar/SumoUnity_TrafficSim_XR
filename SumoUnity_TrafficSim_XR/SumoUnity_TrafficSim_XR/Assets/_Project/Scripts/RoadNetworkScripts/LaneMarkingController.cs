// ==============================
// LaneMarkingController.cs
// ==============================
// Replaces LaneSegmentDecalController. The old component resized a few hundred
// URP DecalProjectors per lane to switch a line between solid and broken.
//
// Here each side is one carrier ribbon mesh (see LaneMarkingMesh.cs) and the
// dash pattern is drawn by the shader, so toggling solid/broken only rewrites
// this lane's vertex colours. No geometry is rebuilt.
//
// Deliberately outside the #if UNITY_EDITOR guard that wraps RoadNetworkBuilder,
// so roads generated in the editor do not turn into missing-script objects in a
// player build.
using System.Collections.Generic;
using UnityEngine;

// No per-frame work: OnValidate alone drives regeneration in the editor.
[DisallowMultipleComponent]
public class LaneMarkingController : MonoBehaviour
{
    [Tooltip("Broken line on the LEFT lane marking (the object named *Right*, matching the original decal naming).")]
    public bool brokenLeft;
    [Tooltip("Broken line on the RIGHT lane marking (the object named *Left*, matching the original decal naming).")]
    public bool brokenRight;

    [Header("Appearance")]
    public Material markingMaterial;
    [Tooltip("Painted width in metres. The ribbon is built wider than this to " +
             "leave room for antialiasing; the material's Marking Width is what " +
             "you actually see, and the two should be kept in step.")]
    public float markingWidth = LaneMarkingMesh.DefaultWidth;

    // Boundary runs already clipped against the junction polygons. Solid/broken
    // is purely a re-dash of these, so toggling never needs the SUMO network.
    [HideInInspector] public List<MarkingSpan> leftSpans = new List<MarkingSpan>();
    [HideInInspector] public List<MarkingSpan> rightSpans = new List<MarkingSpan>();

    private const string LeftChildName = "LaneMarking_Left";
    private const string RightChildName = "LaneMarking_Right";

    // Serialized so a scene load or a domain reload sees them already matching
    // the toggles. Without this, OnValidate would regenerate every marking mesh
    // in the city on every recompile.
    [SerializeField, HideInInspector] private bool prevBrokenLeft;
    [SerializeField, HideInInspector] private bool prevBrokenRight;

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (brokenLeft == prevBrokenLeft && brokenRight == prevBrokenRight) return;
        prevBrokenLeft = brokenLeft;
        prevBrokenRight = brokenRight;

        // Creating and destroying objects directly inside OnValidate is illegal;
        // Unity logs "SendMessage cannot be called during OnValidate". Defer.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            ApplyFlags();
        };
#endif
    }

    public void RebuildAll()
    {
        RebuildSide(LeftChildName, leftSpans, brokenRight);
        RebuildSide(RightChildName, rightSpans, brokenLeft);
    }

    /// <summary>
    /// Flips solid/broken without touching geometry. Falls back to a full
    /// rebuild only if the mesh has not been generated yet.
    /// </summary>
    private void ApplyFlags()
    {
        if (!TrySetFlag(LeftChildName, brokenRight) || !TrySetFlag(RightChildName, brokenLeft))
            RebuildAll();
    }

    private bool TrySetFlag(string childName, bool broken)
    {
        Transform child = transform.Find(childName);
        if (child == null) return false;

        var filter = child.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        LaneMarkingMesh.SetBrokenFlag(filter.sharedMesh, broken);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(filter.sharedMesh);
#endif
        return true;
    }

    private void RebuildSide(string childName, List<MarkingSpan> spans, bool broken)
    {
        Transform child = transform.Find(childName);

        Mesh mesh = LaneMarkingMesh.Build(spans, broken, markingWidth, childName);

        if (mesh == null)
        {
            if (child != null) SafeDestroy(child.gameObject);
            return;
        }

        GameObject go;
        if (child != null)
        {
            go = child.gameObject;
        }
        else
        {
            go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
        }

        var filter = go.GetComponent<MeshFilter>();
        var renderer = go.GetComponent<MeshRenderer>();

        if (filter.sharedMesh != null) SafeDestroy(filter.sharedMesh);
        filter.sharedMesh = mesh;

        if (markingMaterial != null) renderer.sharedMaterial = markingMaterial;

        // Paint is coplanar with the road; casting shadows would produce a dark
        // fringe along every line, and it can never receive a useful one.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(go);
#endif
    }

    private static void SafeDestroy(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
