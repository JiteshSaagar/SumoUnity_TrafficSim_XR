using UnityEditor;
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

        var builder = field.GetComponentInParent<RoadNetworkBuilder>();
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

        EditorGUILayout.LabelField("Live cells", field.LiveCellCount.ToString());
    }
}
