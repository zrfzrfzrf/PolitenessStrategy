using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class PathRecorder : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField, Min(0.02f)] float sampleInterval = 0.5f;
    [SerializeField] bool recordOnStart = true;
    [SerializeField] bool exportOnApplicationPause = true;
    [SerializeField] bool exportOnApplicationQuit = true;
    [SerializeField] KeyCode toggleRecordingKey = KeyCode.R;
    [SerializeField] KeyCode exportKey = KeyCode.P;
    [SerializeField] string filePrefix = "path_recording";
    [SerializeField] string exportDirectory = @"D:\KTH\SummerIntern\Dataset";

    readonly List<PathSample> samples = new List<PathSample>();
    float nextSampleTime;
    bool isRecording;
    int exportedSampleCount;

    public bool IsRecording => isRecording;
    public int SampleCount => samples.Count;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget != null ? newTarget : transform;
    }

    void Awake()
    {
        if (target == null)
        {
            target = transform;
        }
    }

    void Start()
    {
        if (recordOnStart)
        {
            StartRecording();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleRecordingKey))
        {
            if (isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        if (Input.GetKeyDown(exportKey))
        {
            ExportCsv();
        }

        if (!isRecording || target == null || Time.time < nextSampleTime)
        {
            return;
        }

        RecordSample();
        nextSampleTime = Time.time + sampleInterval;
    }

    public void StartRecording()
    {
        isRecording = true;
        nextSampleTime = Time.time;
    }

    public void StopRecording()
    {
        isRecording = false;
    }

    public void ClearSamples()
    {
        samples.Clear();
        exportedSampleCount = 0;
    }

    public string ExportCsv()
    {
        if (samples.Count == 0)
        {
            Debug.LogWarning("PathRecorder has no samples to export.");
            return string.Empty;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{filePrefix}_{timestamp}.csv";
        string directory = GetExportDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        var csv = new StringBuilder();
        csv.AppendLine("sample_index,elapsed_seconds,local_time_iso8601,x,y,z,rotation_x,rotation_y,rotation_z");

        for (int i = 0; i < samples.Count; i++)
        {
            PathSample sample = samples[i];
            csv.Append(i).Append(',');
            csv.Append(sample.ElapsedSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.LocalTimeIso8601).Append(',');
            csv.Append(sample.Position.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.Position.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.Position.z.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.Rotation.eulerAngles.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.Rotation.eulerAngles.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.Rotation.eulerAngles.z.ToString("F3", CultureInfo.InvariantCulture));
            csv.AppendLine();
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
        exportedSampleCount = samples.Count;
        Debug.Log($"PathRecorder exported {samples.Count} samples to: {path}");
        return path;
    }

    string GetExportDirectory()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Application.persistentDataPath;
#else
        return string.IsNullOrWhiteSpace(exportDirectory)
            ? Application.persistentDataPath
            : exportDirectory;
#endif
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            if (exportOnApplicationPause)
            {
                ExportIfNeeded();
            }

            return;
        }

        if (recordOnStart)
        {
            StartRecording();
        }
    }

    void OnApplicationQuit()
    {
        if (exportOnApplicationQuit)
        {
            ExportIfNeeded();
        }
    }

    void ExportIfNeeded()
    {
        if (samples.Count > exportedSampleCount)
        {
            ExportCsv();
        }
    }

    void RecordSample()
    {
        samples.Add(new PathSample
        {
            ElapsedSeconds = Time.time,
            LocalTimeIso8601 = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            Position = target.position,
            Rotation = target.rotation
        });
    }

    struct PathSample
    {
        public float ElapsedSeconds;
        public string LocalTimeIso8601;
        public Vector3 Position;
        public Quaternion Rotation;
    }
}
