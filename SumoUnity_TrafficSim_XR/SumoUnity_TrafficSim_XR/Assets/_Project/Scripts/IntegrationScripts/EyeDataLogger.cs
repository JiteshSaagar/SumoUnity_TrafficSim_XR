using UnityEngine;
using System.IO;

public class EyeDataLogger : MonoBehaviour
{
    [Header("Target to Track")]
    public Transform eyeGazeTransform;

    [Header("File Settings")]
    public string fileName = "EyeTrackingLog.csv";
    private StreamWriter writer;
    private string filePath;

    [Header("Sampling Settings")]
    public float sampleRate = 0.1f;
    private float timer = 0f;

    // This will keep track of a clean, manual timeline (0.0, 0.1, 0.2...)
    private float customTimestamp = 0f;

    void Start()
    {
        string folderPath = @"C:\Users\Guest1\Desktop\JiteshSaagar\SumoUnity_TrafficSim_XR\SumoUnity_TrafficSim_XR\Results\";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(filePath, false);
        writer.WriteLine("Time,PosX,PosY,PosZ,RotX,RotY,RotZ");

        Debug.Log("Eye Data is saving to: " + filePath);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= sampleRate)
        {
            if (writer != null && eyeGazeTransform != null)
            {
                Vector3 pos = eyeGazeTransform.position;
                Vector3 rot = eyeGazeTransform.rotation.eulerAngles;

                // "F1" forces the float to print out with exactly 1 decimal place
                writer.WriteLine($"{customTimestamp:F1},{pos.x},{pos.y},{pos.z},{rot.x},{rot.y},{rot.z}");
            }

            // 1. Advance our custom clock by exactly our step rate
            customTimestamp += sampleRate;

            // 2. Subtract sampleRate instead of resetting to 0f to account for slight frame overflows
            timer -= sampleRate;
        }
    }

    void OnApplicationQuit()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
        }
    }
}