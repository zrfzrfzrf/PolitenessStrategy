using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class DataExport : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField, Min(0.02f)] float sampleInterval = 0.5f;
    [SerializeField] bool recordFromStart = true;
    [SerializeField] bool exportOnApplicationPause = true;
    [SerializeField] bool exportOnApplicationQuit = true;
    [SerializeField] bool ClearPreviousRecordWhenExport = false;
    [SerializeField] KeyCode toggleRecordingKey = KeyCode.R;
    [SerializeField] KeyCode exportKey = KeyCode.P;
    [SerializeField] string filePrefix = "Path_recording";
    [SerializeField] string exportDirectory = @"D:\KTH\SummerIntern\Dataset";

    readonly List<PathSample> samples = new List<PathSample>();
    readonly List<ZoneEvent> zoneEvents = new List<ZoneEvent>();
    float nextSampleTime;
    bool isRecording;
    bool hasPreviousSample;
    Vector3 lastSamplePosition;
    float lastSampleElapsed;
    public bool IsRecording => isRecording;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget != null ? newTarget : transform;
    }

    void OnEnable()
    {
        ZoneEventBus.OnZoneEvent += HandleZoneEvent;
    }

    void OnDisable()
    {
        ZoneEventBus.OnZoneEvent -= HandleZoneEvent;
    }

    void Start()
    {
        if (target == null)
        {
            target = transform;
        }
        if (recordFromStart)
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
        hasPreviousSample = false;
        nextSampleTime = Time.time;
    }

    public void StopRecording()
    {
        isRecording = false;
    }

    public string ExportCsv()
    {
        if (samples.Count == 0)
        {
            Debug.LogWarning("DataExport has no samples to export.");
            return string.Empty;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{filePrefix}_{timestamp}.csv";
        string directory = GetExportDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        int sampleCount = samples.Count;
        var csv = new StringBuilder();
        csv.AppendLine(
            "sample_index,elapsed_seconds,local_time_iso8601,x,y,z,rotation_x,rotation_y,rotation_z,speed_mps,is_in_C,is_in_F,is_in_O");

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
            csv.Append(sample.Rotation.eulerAngles.z.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.SpeedMps.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(sample.IsInC ? '1' : '0').Append(',');
            csv.Append(sample.IsInF ? '1' : '0').Append(',');
            csv.Append(sample.IsInO ? '1' : '0');
            csv.AppendLine();
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);

        string eventsPath = Path.Combine(directory, $"{filePrefix}_events_{timestamp}.csv");
        ExportZoneEventsCsv(eventsPath);

        if (ClearPreviousRecordWhenExport)
        {
            samples.Clear();
            zoneEvents.Clear();
        }

        Debug.Log($"DataExport exported {sampleCount} samples to: {path}");
        return path;
    }

    void ExportZoneEventsCsv(string path)
    {
        var csv = new StringBuilder();
        csv.AppendLine("elapsed_seconds,local_time_iso8601,zone,event_type");

        for (int i = 0; i < zoneEvents.Count; i++)
        {
            ZoneEvent zoneEvent = zoneEvents[i];
            csv.Append(zoneEvent.ElapsedSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(zoneEvent.LocalTimeIso8601).Append(',');
            csv.Append(zoneEvent.Zone).Append(',');
            csv.Append(zoneEvent.EventType);
            csv.AppendLine();
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
        Debug.Log($"DataExport exported {zoneEvents.Count} zone events to: {path}");
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
            if (exportOnApplicationPause && samples.Count != 0)
            {
                ExportCsv();
            }

            return;
        }

        if (recordFromStart)
        {
            StartRecording();
        }
    }

    void OnApplicationQuit()
    {
        if (exportOnApplicationQuit && samples.Count != 0)
        {
            ExportCsv();
        }
    }

    void HandleZoneEvent(ZoneEvent zoneEvent)
    {
        if (!isRecording)
        {
            return;
        }

        zoneEvents.Add(zoneEvent);
    }

    void RecordSample()
    {
        float elapsed = Time.time;
        Vector3 position = target.position;
        float speedMps = 0f;

        if (hasPreviousSample)
        {
            Vector3 delta = position - lastSamplePosition;
            delta.y = 0f;
            float deltaTime = elapsed - lastSampleElapsed;
            if (deltaTime > 0f)
            {
                speedMps = delta.magnitude / deltaTime;
            }
        }

        samples.Add(new PathSample
        {
            ElapsedSeconds = elapsed,
            LocalTimeIso8601 = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            Position = position,
            Rotation = target.rotation,
            SpeedMps = speedMps,
            IsInC = ZoneEventBus.InC,
            IsInF = ZoneEventBus.InF,
            IsInO = ZoneEventBus.InO
        });

        lastSamplePosition = position;
        lastSampleElapsed = elapsed;
        hasPreviousSample = true;
    }

    struct PathSample
    {
        public float ElapsedSeconds;
        public string LocalTimeIso8601;
        public Vector3 Position;
        public Quaternion Rotation;
        public float SpeedMps;
        public bool IsInC;
        public bool IsInF;
        public bool IsInO;
    }
}
