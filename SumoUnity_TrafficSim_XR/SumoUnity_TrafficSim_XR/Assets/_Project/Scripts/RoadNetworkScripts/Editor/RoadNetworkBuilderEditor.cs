using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds grass maintenance actions to the manager inspector.
///
/// The manager's <c>grassSettings</c> is the authoritative copy: on every
/// domain reload - which includes entering Play mode - <c>OnValidate</c> pushes
/// it down into the generated <c>GrassField</c>. Resetting only the field is
/// therefore pointless, because the manager overwrites it the moment you press
/// Play. Everything here operates on the manager first.
/// </summary>
[CustomEditor(typeof(RoadNetworkBuilder))]
public class RoadNetworkBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var builder = (RoadNetworkBuilder)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Grass Maintenance", EditorStyles.boldLabel);

        // A GrassField component on the manager itself is not the generated one
        // and renders nothing, because it has no terrain polygons. It is also a
        // trap: GetComponentInChildren includes self, so stale code could latch
        // onto it and every grass edit would vanish into it.
        var stray = builder.GetComponent<GrassField>();
        if (stray != null)
        {
            EditorGUILayout.HelpBox(
                "There is a GrassField component on this Managers object. The real, " +
                "generated grass field lives under RoadNetworkRoot. This one has no " +
                "terrain polygons, renders nothing, and only causes confusion.",
                MessageType.Warning);

            if (GUILayout.Button("Remove Stray GrassField From This Object"))
            {
                Undo.DestroyObjectImmediate(stray);
                MarkDirty(builder);
                return;
            }
        }

        EditorGUILayout.HelpBox(
            "Editing a default in GrassSettings.cs does NOT change this scene. " +
            "Unity runs field initialisers only when a component is first created, " +
            "then restores the saved values on every load. Use the button below to " +
            "adopt new script defaults, then save the scene.",
            MessageType.Info);

        if (GUILayout.Button("Reset Grass To Script Defaults"))
        {
            ResetGrassToScriptDefaults(builder);
            return;
        }

        if (GUILayout.Button("Push Grass Settings To Field Now"))
        {
            var field = builder.GrassFieldRef;
            if (field == null)
            {
                Debug.LogWarning("[RoadNetworkBuilder] No generated GrassField found. " +
                                 "Rebuild the road network first.");
            }
            else
            {
                Undo.RecordObject(field, "Push Grass Settings");
                field.ApplySettings(builder.grassSettings);
                EditorUtility.SetDirty(field);
                MarkDirty(builder);
                SceneView.RepaintAll();
            }
        }
    }

    /// <summary>
    /// Overwrites the serialised grass settings on the manager - and on the
    /// generated field - with a freshly constructed <see cref="GrassSettings"/>,
    /// so the values written in the script become the values in the scene.
    /// </summary>
    private static void ResetGrassToScriptDefaults(RoadNetworkBuilder builder)
    {
        var fresh = new GrassSettings();

        // The material is a scene reference, not a script default. A plain reset
        // would null it, and GrassField.Update returns early on a null material,
        // so the grass would silently disappear with no error.
        Material keep = builder.grassSettings != null ? builder.grassSettings.material : null;
        var field = builder.GrassFieldRef;
        if (keep == null && field != null && field.settings != null) keep = field.settings.material;
        fresh.material = keep;

        Undo.RecordObject(builder, "Reset Grass To Script Defaults");
        builder.grassSettings = fresh;
        EditorUtility.SetDirty(builder);

        if (field != null)
        {
            Undo.RecordObject(field, "Reset Grass To Script Defaults");
            field.ApplySettings(fresh);
            EditorUtility.SetDirty(field);
        }

        MarkDirty(builder);
        SceneView.RepaintAll();

        Debug.Log("[RoadNetworkBuilder] Grass reset to script defaults " +
                  $"(density {fresh.density}, height {fresh.height}, " +
                  $"viewRadius {fresh.viewRadius}, forwardBias {fresh.forwardBias}, " +
                  $"roadMargin {fresh.roadMargin}). SAVE THE SCENE to keep them.");
    }

    private static void MarkDirty(Object obj)
    {
        if (Application.isPlaying) return;
        var comp = obj as Component;
        if (comp != null) EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
    }
}
