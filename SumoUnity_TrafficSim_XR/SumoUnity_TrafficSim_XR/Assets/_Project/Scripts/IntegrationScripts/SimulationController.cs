using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

public class SimulationController : MonoBehaviour
{
    private ExchangeData _ExchangeData;
    private GameObject vehiclePrefab;
    private Dictionary<string, GameObject> vehicleObjects = new Dictionary<string, GameObject>();
    private string vehicleDataJson = "{}";
    private object vehicleDataLock = new object();
    
    [Header("Ego Settings")]
    public GameObject egoVehicle;
    public string egoVehicleId = "f_0.0";
    [Tooltip("Check this if your ego is a VR Pedestrian so SUMO knows how to process it. " +
             "The backend then injects it as a SUMO person instead of a vehicle, so set " +
             "Ego Vehicle Id to a person id (e.g. xr_ped), NOT a trip from the route file.")]
    public bool isPedestrian = false;

    [Tooltip("Speed ceiling for a pedestrian ego, in m/s. An XR rig recentre or " +
             "teleport moves the camera metres in one frame, which differencing " +
             "would report to SUMO as tens of m/s. Sprinting is about 6 m/s.")]
    public float maxPedestrianSpeed = 6f;
    [Tooltip("Check this if the Ego is already in the scene (like an XR Origin) so Unity doesn't clone it.")]
    public bool isSceneObject = false; 
    public Vector3 egoVehicleInitialPosition = new Vector3(0f, 0f, 0f);
    public Quaternion egoVehicleInitialRotation = Quaternion.Euler(0f, 90f, 0f);

    private GameObject f_1_0;
    private Vector3 previousPosition;
    private Vector3 currentPosition;
    private float long_speed;
    private float distanceAccumulator = 0f;
    private float timeAccumulator = 0f;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    private StreamWriter writer;

    [Header("Unity Step Length (seconds)")]
    public float unityStepLength = 0.10f;

    private float fixedTimeAccum = 0f; 

    private bool firstTimestampLogged = false;
    private float firstLoggedTime = 0f;

    [Header("Add all Junction GameObjects")]
    public GameObject junctions;           
    private readonly Dictionary<string, GameObject> junctionCache = new();

    [Serializable]
    public class Vehicle
    {
        public string vehicle_id;
        public double[] position;
        public double angle;
        public string type;
        public float long_speed;
        public float vert_speed;
        public float lat_speed;
    }

    [Serializable]
    private class VehicleWrapper
    {
        public Vehicle[] vehicles;
    }

    [Serializable]
    public class Person
    {
        public string person_id;
        public double[] position;
        public double angle;
        public string type;      // SUMO vType, e.g. "ped_adult"
        public float speed;      // m/s, drives the walk animation
        public string road_id;
        public string state;     // "sidewalk" | "crossing" | "walkingarea" | "internal"
    }

    [Serializable]
    private class PersonWrapper
    {
        public Person[] persons;
    }

    [Serializable]
    public class PedestrianModel
    {
        [Tooltip("SUMO vType id, e.g. ped_adult / ped_slow / ped_xr.")]
        public string sumoPersonType;
        public GameObject unityPedestrianPrefab;
    }

    [Serializable]
    public class TrafficLight
    {
        public string junction_id;
        public string state;
    }

    [Serializable]
    private class TrafficLightsWrapper
    {
        public TrafficLight[] lights;
    }

    [System.Serializable]
    public class CarModel
    {
        public string sumoVehicleType;
        public GameObject unityVehiclePrefab;
    }

    [Header("Add Unity Vehicle Prefab (3DModel) according to Sumo Vehicle Type")]
    public List<CarModel> carModelsList = new List<CarModel>();

    [Header("Pedestrians")]
    [Tooltip("Fallback prefab for any SUMO person whose vType is not mapped " +
             "below. Leave empty to fall back to a capsule placeholder.")]
    public GameObject pedestrianPrefab;

    [Tooltip("Add a Unity pedestrian prefab per SUMO person vType.")]
    public List<PedestrianModel> pedestrianModelsList = new List<PedestrianModel>();

    [Tooltip("Heading offset applied to pedestrians, in degrees. 0 suits a " +
             "humanoid whose model faces +Z (the Unity and Mixamo convention). " +
             "Vehicles use -90 because the car models face +X.")]
    public float pedestrianHeadingOffset = 0f;

    private readonly Dictionary<string, GameObject> personObjects = new Dictionary<string, GameObject>();

    // Phase 4 raised sidewalks above the carriageway, but SUMO reports every
    // pedestrian at z = 0. Without lifting them onto the kerb they walk sunk
    // into the pavement. Resolved once at Start from the generated road network.
    private WalkableAreas _walkableAreas;

    private float _lastTlTime = 0f;
    private float tlUpdateInterval = 1f;
    private Dictionary<string, string> _lastTlState = new();

    private static string LocateOrCreateResultsFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        DirectoryInfo dir = new DirectoryInfo(projectRoot);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Results");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;                       
        }

        string fallback = Path.Combine(projectRoot, "Results");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private void Start()
    {
        vehiclePrefab = Resources.Load("EloraGold") as GameObject;

        if (vehiclePrefab == null)
        {
            Debug.LogError("Vehicle prefab 'EloraGold' not found in Resources.");
            return;
        }

        _ExchangeData = GetComponent<ExchangeData>();
        if (_ExchangeData == null)
        {
            _ExchangeData = gameObject.AddComponent<ExchangeData>();
        }

        _walkableAreas = FindFirstObjectByType<WalkableAreas>();

        SumoRequesterStart();

        string sumoDataDir = LocateOrCreateResultsFolder();
        string logPath = Path.Combine(sumoDataDir, "vehicle_data_report.txt");
        writer = new StreamWriter(logPath, append: false, Encoding.UTF8);
        writer.WriteLine("timestep_time;vehicle_id;vehicle_x;vehicle_y;vehicle_z");
    }

    public void SumoRequesterStart()
    {
        if (egoVehicle == null)
        {
            Debug.LogError("Ego vehicle GameObject is not assigned.");
            return;
        }

        if (!isSceneObject)
        {
            egoVehicle = GameObject.Instantiate(egoVehicle, egoVehicleInitialPosition, egoVehicleInitialRotation);
        }
        else
        {
            egoVehicle.transform.position = egoVehicleInitialPosition;
            egoVehicle.transform.rotation = egoVehicleInitialRotation;
        }

        egoVehicle.name = egoVehicleId;
        vehicleObjects.Add(egoVehicleId, egoVehicle);
        previousPosition = egoVehicle.transform.position;
    }

    void Update()
    {
        try
        {
            string data = CollectVehicleData();
            lock (vehicleDataLock)
            {
                vehicleDataJson = data;
            }

            while (mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in Update(): {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void FixedUpdate()
    {
        if (!RecordingManager.startRecordingFromZero)
        {
            return;
        }

        fixedTimeAccum += Time.fixedDeltaTime;
        if (fixedTimeAccum >= unityStepLength - 0.002)
        {
            float currentTime = Time.fixedTime;

            if (!firstTimestampLogged)
            {
                firstLoggedTime = currentTime;
                firstTimestampLogged = true;
            }

            float logTime = currentTime - firstLoggedTime;
            LogVehicleData(logTime);
            fixedTimeAccum = 0f;
        }
    }

    private void LogVehicleData(float relativeLogTime)
    {
        foreach (var kvp in vehicleObjects)
        {
            string vehicleId = kvp.Key;
            GameObject vehicleObj = kvp.Value;
            Vector3 pos = vehicleObj.transform.position;
            writer.WriteLine($"{relativeLogTime:F3};{vehicleId};{pos.x:F2};{pos.y:F2};{pos.z:F2}");
        }
    }

    private void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }

    public void EnqueueMainThreadAction(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    public string CollectVehicleData()
    {
        if (!vehicleObjects.ContainsKey(egoVehicleId))
        {
            UnityEngine.Debug.LogWarning("Ego vehicle not found. Sending empty JSON.");
            return "{}";
        }

        GameObject egoObj = vehicleObjects[egoVehicleId];
        currentPosition = egoObj.transform.position;
        
        // Safely calculate velocity manually to prevent crashes on objects without Rigidbodies
        float dt = Time.deltaTime > 0f ? Time.deltaTime : 0.02f;
        Vector3 velocity = (currentPosition - previousPosition) / dt;

        float vertical_speed = 0f;
        float lateral_speed = 0f;

        Rigidbody rb = egoObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            long_speed = rb.linearVelocity.magnitude;
            vertical_speed = rb.linearVelocity.y;
            lateral_speed = rb.linearVelocity.z;
        }
        else
        {
            long_speed = velocity.magnitude;
            vertical_speed = velocity.y;
            lateral_speed = velocity.z;
        }

        previousPosition = currentPosition;

        // An XR rig has no Rigidbody, so speed comes from differencing the camera
        // position. A recentre, a teleport or a dropped frame moves it metres in
        // one step and would emit an absurd speed into SUMO's telemetry; clamp it
        // to something a human could actually do.
        if (isPedestrian)
        {
            long_speed = Mathf.Min(long_speed, maxPedestrianSpeed);
            vertical_speed = Mathf.Clamp(vertical_speed, -maxPedestrianSpeed, maxPedestrianSpeed);
            lateral_speed = Mathf.Clamp(lateral_speed, -maxPedestrianSpeed, maxPedestrianSpeed);
        }

        float unroundangle = egoObj.transform.rotation.eulerAngles.y;
        double angle = Math.Round(unroundangle, 2);
        double x = Math.Round(currentPosition.x, 2);
        double y = Math.Round(currentPosition.z, 2);
        double z = Math.Round(currentPosition.y, 2);
        
        string type = isPedestrian ? "pedestrian" : "ego";

        Vehicle egoVehicleData = new Vehicle();
        egoVehicleData.vehicle_id = egoVehicleId;
        egoVehicleData.position = new double[] { x, y, z };
        egoVehicleData.angle = angle;
        egoVehicleData.type = type;
        egoVehicleData.long_speed = (float)Math.Round(long_speed, 2);
        egoVehicleData.vert_speed = (float)Math.Round(vertical_speed, 2);
        egoVehicleData.lat_speed = (float)Math.Round(lateral_speed, 2);

        string jsonData = JsonHelper.ToJson(new Vehicle[] { egoVehicleData });
        return jsonData;
    }

    public string GetVehicleDataJson()
    {
        lock (vehicleDataLock)
        {
            return vehicleDataJson;
        }
    }

    public void HandleMessage(string message)
    {
        CommonMessage common = JsonUtility.FromJson<CommonMessage>(message);

        if (common == null || string.IsNullOrEmpty(common.type))
        {
            Debug.LogError("Received message with no type field or invalid JSON.");
            return;
        }

        if (common.type == "command")
        {
            if (common.command == "START_RECORDING")
            {
                RecordingManager.startRecordingFromZero = true;
                RecordingManager.recordingStartTime = Time.time;
                Debug.Log("Received START_RECORDING command from SUMO. Starting logs from zero now.");

                firstTimestampLogged = false;
                firstLoggedTime = 0f;
            }
            else if (common.command == "STOP_RECORDING")
            {
                RecordingManager.startRecordingFromZero = false;
                Debug.Log("Received STOP_RECORDING command from SUMO. Stopping logs.");

                var nonEgoKeys = vehicleObjects.Keys.Where(k => k != egoVehicleId).ToList();
                foreach (var vid in nonEgoKeys)
                {
                    GameObject obj = vehicleObjects[vid];
                    Destroy(obj);
                    vehicleObjects.Remove(vid);
                }

                foreach (var kvp in personObjects)
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                }
                personObjects.Clear();
            }

            return; 
        }
        else if (common.type == "vehicles")
        {
            VehicleWrapper wrapper = JsonUtility.FromJson<VehicleWrapper>(message);
            Vehicle[] vehicleArray = wrapper.vehicles;
            List<Vehicle> vehiclesData = vehicleArray != null ? vehicleArray.ToList() : new List<Vehicle>();

            HashSet<string> incomingVehicleIds = new HashSet<string>(vehiclesData.Select(v => v.vehicle_id));
            var vehiclesToRemove = vehicleObjects.Keys.Where(id => !incomingVehicleIds.Contains(id) && id != egoVehicleId).ToList();

            foreach (var id in vehiclesToRemove)
            {
                GameObject vehicleToDestroy = vehicleObjects[id];
                GameObject.Destroy(vehicleToDestroy);
                vehicleObjects.Remove(id);
            }

            foreach (var vehicle in vehiclesData)
            {
                Vector3 newPosition = new Vector3((float)vehicle.position[0], (float)vehicle.position[2], (float)vehicle.position[1]);
                Quaternion newRotation = Quaternion.Euler(0, (float)vehicle.angle - 90f, 0);
                float vehicleSpeed = vehicle.long_speed;
                float vehiclevertical_speed = vehicle.vert_speed;
                float vehiclelateral_speed = vehicle.lat_speed;

                if (vehicle.vehicle_id == egoVehicleId)
                {
                    continue;
                }

                if (vehicleObjects.ContainsKey(vehicle.vehicle_id))
                {
                    GameObject existingVehicle = vehicleObjects[vehicle.vehicle_id];
                    VehicleController vehicleController = existingVehicle.GetComponent<VehicleController>();
                    if (vehicleController != null)
                    {
                        vehicleController.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    }
                }
                else
                {
                    GameObject prefabToInstantiate = vehiclePrefab;
                    foreach (CarModel carModel in carModelsList)
                    {
                        if (carModel.sumoVehicleType == vehicle.type)
                        {
                            prefabToInstantiate = carModel.unityVehiclePrefab;
                            break;
                        }
                    }

                    GameObject newVehicle = GameObject.Instantiate(prefabToInstantiate, newPosition, newRotation);
                    newVehicle.name = vehicle.vehicle_id;
                    VehicleController vc = newVehicle.GetComponent<VehicleController>();
                    if (vc == null)
                    {
                        vc = newVehicle.AddComponent<VehicleController>();
                    }

                    vc.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    vehicleObjects.Add(vehicle.vehicle_id, newVehicle);
                }
            }
        }
        else if (common.type == "persons")
        {
            HandlePersonsMessage(message);
        }
        else if (common.type == "trafficlights")
        {
            var wrapper = JsonUtility.FromJson<TrafficLightsWrapper>(message);

            foreach (var tl in wrapper.lights)
            {
                if (!_lastTlState.TryGetValue(tl.junction_id, out var prev)
                 || prev != tl.state)
                {
                    ChangeTrafficStatus(tl.junction_id, tl.state);
                    _lastTlState[tl.junction_id] = tl.state;
                }
            }
        }
        else
        {
            Debug.LogWarning("Received message with unknown type: " + common.type);
        }
    }

    public void EnqueueOnMainThread(string message)
    {
        EnqueueMainThreadAction(() => HandleMessage(message));
    }

    /// <summary>
    /// Spawns, moves and retires SUMO's pedestrians. Mirrors the vehicles branch,
    /// but keeps a separate dictionary so a person is never fed to
    /// VehicleController and never collides with the ego-vehicle bookkeeping.
    /// </summary>
    private void HandlePersonsMessage(string message)
    {
        PersonWrapper wrapper = JsonUtility.FromJson<PersonWrapper>(message);
        if (wrapper == null || wrapper.persons == null) return;

        HashSet<string> incoming = new HashSet<string>();
        foreach (var p in wrapper.persons)
        {
            if (p != null && !string.IsNullOrEmpty(p.person_id)) incoming.Add(p.person_id);
        }

        // Retire pedestrians SUMO no longer reports: they either arrived at
        // their destination or walked outside the subscription radius.
        var stale = personObjects.Keys.Where(id => !incoming.Contains(id)).ToList();
        foreach (var id in stale)
        {
            if (personObjects[id] != null) Destroy(personObjects[id]);
            personObjects.Remove(id);
        }

        foreach (var person in wrapper.persons)
        {
            if (person == null || string.IsNullOrEmpty(person.person_id)) continue;
            if (person.position == null || person.position.Length < 3) continue;

            // The XR tester is driven by the rig itself, not by SUMO echoing our
            // own position back at us. Without this the human would fight a
            // puppet standing in the same place (Phase 2).
            if (isPedestrian && person.person_id == egoVehicleId) continue;

            // SUMO (x, y, z) -> Unity (x, z_up, y), matching the vehicles branch.
            Vector3 pos = new Vector3(
                (float)person.position[0],
                (float)person.position[2] + SurfaceHeightFor(person.state),
                (float)person.position[1]);
            Quaternion rot = Quaternion.Euler(0f, (float)person.angle + pedestrianHeadingOffset, 0f);

            if (!personObjects.TryGetValue(person.person_id, out GameObject go) || go == null)
            {
                go = SpawnPedestrian(person, pos, rot);
                if (go == null) continue;
                personObjects[person.person_id] = go;
            }

            var pc = go.GetComponent<PedestrianController>();
            if (pc != null) pc.UpdateTarget(pos, rot, person.speed);
        }
    }

    /// <summary>
    /// How far to lift a pedestrian so their feet meet the surface they are on.
    /// Sidewalks and walking areas are raised onto a kerb by the road builder;
    /// crossings stay at carriageway level, which is what a real zebra does.
    /// </summary>
    private float SurfaceHeightFor(string state)
    {
        if (_walkableAreas == null) return 0f;
        return (state == "sidewalk" || state == "walkingarea")
            ? _walkableAreas.surfaceHeight
            : 0f;
    }

    private GameObject SpawnPedestrian(Person person, Vector3 pos, Quaternion rot)
    {
        GameObject prefab = pedestrianPrefab;
        foreach (var model in pedestrianModelsList)
        {
            if (model != null && model.sumoPersonType == person.type && model.unityPedestrianPrefab != null)
            {
                prefab = model.unityPedestrianPrefab;
                break;
            }
        }

        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, pos, rot);
        }
        else
        {
            // No humanoid assigned yet: a capsule keeps the whole channel
            // testable, so the data path can be verified before art exists.
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.9f, rot);
            Collider capsuleCollider = go.GetComponent<Collider>();
            if (capsuleCollider != null) Destroy(capsuleCollider);
        }

        go.name = "ped_" + person.person_id;
        if (go.GetComponent<PedestrianController>() == null) go.AddComponent<PedestrianController>();
        return go;
    }

    private void ChangeTrafficStatus(string junctionID, string state)
    {
        if (!junctionCache.TryGetValue(junctionID, out GameObject junctionGO))
        {
            var t = junctions.transform.Find(junctionID);
            if (t == null) { Debug.LogWarning($"Junction {junctionID} not found"); return; }
            junctionGO = t.gameObject;
            junctionCache[junctionID] = junctionGO;
        }

        for (int i = 0; i < state.Length; i++)
        {
            var headTransform = junctionGO.transform.Find($"Head{i}");
            if (headTransform == null)
            {
                continue;
            }
            SetSignalState(state[i], headTransform.gameObject);
        }
    }

    private void SetSignalState(char c, GameObject head)
    {
        var green = FindChildRecursive(head.transform, "green_light");
        var yellow = FindChildRecursive(head.transform, "yellow_light");
        var red = FindChildRecursive(head.transform, "red_light");
        if (green) green.SetActive(c == 'G' || c == 'g');
        if (yellow) yellow.SetActive(c == 'y' || c == 'Y');
        if (red) red.SetActive(!(c == 'G' || c == 'g' || c == 'y' || c == 'Y'));
    }

    private GameObject FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child.gameObject;
            var found = FindChildRecursive(child, name);
            if (found) return found;
        }
        return null;
    }

    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            string newJson = "{ \"vehicles\": " + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.vehicles;
        }

        public static string ToJson<T>(T[] array)
        {
            Wrapper<T> wrapper = new Wrapper<T> { vehicles = array };
            return JsonUtility.ToJson(wrapper);
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] vehicles;
        }
    }
}