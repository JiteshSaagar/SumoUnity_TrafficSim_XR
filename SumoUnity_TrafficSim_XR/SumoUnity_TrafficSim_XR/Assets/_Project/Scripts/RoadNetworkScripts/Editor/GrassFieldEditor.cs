using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Points people at the manager object rather than letting them tune grass in
/// two places and wonder why their edits keep reverting.
/// </summary>
[CustomEditor(typeof(GrassField))]
public class GrassFieldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var field = (GrassField)target;

        EditorGUILayout.HelpBox(
            "Grass is authored on the RoadNetworkBuilder manager object, under " +
            "\"Grass\". Editing it there applies immediately - no road rebuild - " +
            "and overwrites whatever is shown below.",
            MessageType.Info);

        var builder = FindBuilderFor(field);
        if (builder != null && GUILayout.Button("Select Manager Object"))
        {
            Selection.activeGameObject = builder.gameObject;
            return;
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Rebuild Grass Now"))
        {
            field.Rebuild();
            SceneView.RepaintAll();
        }

        // Editing a default in GrassSettings.cs does NOT reach a component that
        // is already saved in a scene: Unity runs field initialisers only when
        // the instance is first constructed, then restores the serialised values
        // on every load. This button is the way to adopt new script defaults.
        if (GUILayout.Button("Reset Grass To Script Defaults"))
        {
            ResetToScriptDefaults(field, builder);
            SceneView.RepaintAll();
            return;
        }

        EditorGUILayout.LabelField("Live cells", field.LiveCellCount.ToString());
    }

    /// <summary>
    /// Locates the manager that drives this field.
    ///
    /// GetComponentInParent is not enough: the generated field lives under
    /// RoadNetworkRoot, a separate scene root, so walking up from the field
    /// never reaches the Managers object and the manager's settings would be
    /// left stale - which the next Play would push straight back over the top.
    /// </summary>
    private static RoadNetworkBuilder FindBuilderFor(GrassField field)
    {
        var direct = field.GetComponentInParent<RoadNetworkBuilder>();
        if (direct != null) return direct;

        var all = Object.FindObjectsByType<RoadNetworkBuilder>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var b in all)
        {
            if (b.GrassFieldRef == field) return b;
        }
        return all.Length > 0 ? all[0] : null;
    }

    /// <summary>
    /// Overwrites the serialised grass settings - on the manager and on the
    /// field - with a freshly constructed <see cref="GrassSettings"/>, so the
    /// values written in the script become the values in the scene.
    /// </summary>
    private static void ResetToScriptDefaults(GrassField field, RoadNetworkBuilder builder)
    {
        var fresh = new GrassSettings();

        // The material is a scene reference, not a script default, so a plain
        // reset would null it and the grass would silently stop drawing
        // (GrassField.Update returns early on a null material). Carry it over.
        Material keep = null;
        if (builder != null && builder.grassSettings != null) keep = builder.grassSettings.material;
        if (keep == null && field.settings != null) keep = field.settings.material;
        fresh.material = keep;

        if (builder != null)
        {
            Undo.RecordObject(builder, "Reset Grass To Script Defaults");
            builder.grassSettings = fresh.Clone();
            EditorUtility.SetDirty(builder);
        }
        else
        {
            // Without the manager this reset lasts only until the next domain
            // reload, because OnValidate pushes the manager's copy back down.
            Debug.LogWarning(
                "[GrassField] No RoadNetworkBuilder found in the scene. Only this " +
                "field was reset, and entering Play mode will overwrite it from " +
                "the manager's saved settings.");
        }

        Undo.RecordObject(field, "Reset Grass To Script Defaults");
        field.ApplySettings(fresh);
        EditorUtility.SetDirty(field);

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(field.gameObject.scene);

        Debug.Log(
            "[GrassField] Grass settings reset to script defaults " +
            $"(density {fresh.density}, height {fresh.height}, viewRadius {fresh.viewRadius}, " +
            $"forwardBias {fresh.forwardBias}). Save the scene to keep them.");
    }
}
