using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the pavement material used by Phase 4 sidewalks and walking areas.
///
/// Generated rather than committed as .mat YAML so Unity resolves the render
/// pipeline's shader itself. A hand-written material pins a shader GUID, which
/// is exactly the sort of thing that renders magenta on a different pipeline.
/// </summary>
public static class SidewalkMaterialSetup
{
    private const string MaterialDir = "Assets/_Project/Materials";
    private const string MaterialPath = MaterialDir + "/Mat_Sidewalk.mat";
    private const string TextureDir = "Assets/_Project/Textures";

    // Candidates in preference order; the project ships all three.
    private static readonly string[] TextureCandidates =
    {
        TextureDir + "/SidewalkMaterial1.png",
        TextureDir + "/SidewalkMaterial.jpg",
        TextureDir + "/SidewalkMaterial2.jpg",
    };

    [MenuItem("Sumo2Unity/5. Create Sidewalk Material")]
    public static void CreateSidewalkMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[Sidewalk] No usable shader found.");
            return;
        }

        Directory.CreateDirectory(MaterialDir);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(shader);
        else mat.shader = shader;

        Texture2D tex = null;
        foreach (string path in TextureCandidates)
        {
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) break;
        }

        if (tex != null)
        {
            // URP/Lit uses _BaseMap; Standard uses _MainTex. Set whichever the
            // shader actually declares rather than assuming the pipeline.
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }
        else
        {
            Debug.LogWarning("[Sidewalk] No sidewalk texture found in " + TextureDir +
                             "; the material will be flat colour.");
        }

        // Pavement is rough and non-metallic; the default 0.5 smoothness reads
        // as wet concrete under the scene lighting.
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.12f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

        // World-scale tiling: the sidewalk mesh UVs run along the lane, so a
        // small tiling value keeps slabs from smearing along long straights.
        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", new Vector2(1f, 4f));

        if (isNew) AssetDatabase.CreateAsset(mat, MaterialPath);
        else EditorUtility.SetDirty(mat);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        AssignToBuilder(mat);

        Selection.activeObject = mat;
        Debug.Log($"[Sidewalk] {(isNew ? "Created" : "Updated")} {MaterialPath} " +
                  $"using shader '{shader.name}'" +
                  (tex != null ? $" and texture '{tex.name}'." : ".") +
                  " Rebuild the road network to apply it.");
    }

    /// <summary>
    /// Assigns the material to the road builder if one is in the scene, so the
    /// next rebuild picks it up without a manual inspector step.
    /// </summary>
    private static void AssignToBuilder(Material mat)
    {
        var builder = Object.FindFirstObjectByType<RoadNetworkBuilder>();
        if (builder == null) return;

        Undo.RecordObject(builder, "Assign Sidewalk Material");
        builder.sidewalkMaterial = mat;
        EditorUtility.SetDirty(builder);
        Debug.Log("[Sidewalk] Assigned to RoadNetworkBuilder.sidewalkMaterial.");
    }
}
