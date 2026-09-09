using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the keyboard/mouse pedestrian rig used to test the pedestrian
/// co-simulation without a headset, and wires it into SimulationController.
///
/// The prefab is generated rather than committed as hand-written YAML because
/// Unity itself then serialises the Camera and component references, which is
/// the difference between a prefab that always opens and one that might not.
/// </summary>
public static class DesktopPedestrianSetup
{
    private const string PrefabDir = "Assets/_Project/Prefabs";
    private const string PrefabPath = PrefabDir + "/DesktopPedestrian.prefab";

    // On the E0 sidewalk, west of the J8 zebra crossing, facing east towards it.
    // SUMO (x, y) -> Unity (x, height, y), so SUMO (-45.0, 6.27) is this.
    private static readonly Vector3 SpawnPosition = new Vector3(-45f, 0f, 6.27f);
    private static readonly Vector3 SpawnEuler = new Vector3(0f, 90f, 0f);

    [MenuItem("Sumo2Unity/4. Create Desktop Test Pedestrian")]
    public static void CreateDesktopPedestrian()
    {
        GameObject rig = BuildRig();

        Directory.CreateDirectory(PrefabDir);
        AssetDatabase.Refresh();

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            rig, PrefabPath, InteractionMode.UserAction);

        Undo.RegisterCreatedObjectUndo(rig, "Create Desktop Test Pedestrian");
        Selection.activeGameObject = rig;

        bool wired = TryWireSimulationController(rig);

        EditorSceneManager.MarkSceneDirty(rig.scene);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[DesktopPedestrian] Prefab saved to {PrefabPath} and placed at " +
            $"{SpawnPosition} (E0 sidewalk, west of the J8 zebra).\n" +
            (wired
                ? "SimulationController wired: Is Pedestrian = true, Ego Vehicle Id = xr_ped, " +
                  "Is Scene Object = true, Ego Vehicle = this rig."
                : "No SimulationController found - set Ego Vehicle to this rig by hand.") +
            "\nIn the Python dashboard, tick 'Ego is XR pedestrian'. Then press Play. " +
            "WASD to walk, mouse to look, Esc to release the cursor.");

        if (prefab == null)
            Debug.LogWarning("[DesktopPedestrian] Prefab save returned null; the scene object is still usable.");
    }

    private static GameObject BuildRig()
    {
        var root = new GameObject("DesktopPedestrian");
        root.transform.SetPositionAndRotation(SpawnPosition, Quaternion.Euler(SpawnEuler));

        var camGo = new GameObject("Eye Camera");
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);

        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;   // so a kerb right at the feet does not clip
        cam.farClipPlane = 1500f;

        // GrassField.ResolveCamera falls back to Camera.main in play mode, and
        // Scenario1 currently has no MainCamera at all - so without this tag the
        // grass simply never generates while playing.
        camGo.tag = "MainCamera";

        var controller = root.AddComponent<DesktopPedestrianController>();
        controller.eyeCamera = cam;

        WarnOnExtraMainCameras(camGo);
        return root;
    }

    private static void WarnOnExtraMainCameras(GameObject mine)
    {
        int others = 0;
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.gameObject != mine && c.CompareTag("MainCamera")) others++;
        }
        if (others > 0)
        {
            Debug.LogWarning(
                $"[DesktopPedestrian] {others} other camera(s) are tagged MainCamera. " +
                "Camera.main picks one arbitrarily, so disable the others (the XR rig " +
                "or the ego car) while testing on desktop.");
        }
    }

    private static bool TryWireSimulationController(GameObject rig)
    {
        var sim = Object.FindFirstObjectByType<SimulationController>();
        if (sim == null) return false;

        Undo.RecordObject(sim, "Wire Desktop Test Pedestrian");
        sim.isPedestrian = true;
        sim.isSceneObject = true;          // move the rig in place, do not clone it
        sim.egoVehicle = rig;
        sim.egoVehicleId = "xr_ped";       // a SUMO person id, not a route-file trip
        sim.egoVehicleInitialPosition = SpawnPosition;
        sim.egoVehicleInitialRotation = Quaternion.Euler(SpawnEuler);
        EditorUtility.SetDirty(sim);
        return true;
    }
}
