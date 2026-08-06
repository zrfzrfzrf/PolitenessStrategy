using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExciteOMeter;
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

    [Header("Session time and physiological signals")]
    [Tooltip("When enabled, Path uses the EOM Session clock configured by AutoRecorder. Disable it to keep the independent elapsed-time export.")]
    [SerializeField] bool useSessionTime;
    [SerializeField] bool includePhysiologicalSignals;

    readonly List<PathSample> samples = new List<PathSample>();
    readonly List<ZoneEvent> zoneEvents = new List<ZoneEvent>();
    readonly Queue<TimedSignalValue> ecgQueue = new Queue<TimedSignalValue>();
    readonly Queue<TimedSignalValue> hrQueue = new Queue<TimedSignalValue>();
    readonly Queue<TimedSignalValue> rmssdQueue = new Queue<TimedSignalValue>();
    double nextSampleTime;
    bool isRecording;
    bool hasPreviousSample;
    Vector3 lastSamplePosition;
    double lastSampleElapsed;
    float latestEcg;
    float latestHr;
    float latestRmssd;
    bool hasEcg;
    bool hasHr;
    bool hasRmssd;
    public bool IsRecording => isRecording;
    bool ShouldIncludePhysiologicalSignals =>
        includePhysiologicalSignals || string.Equals(filePrefix, "Path_recording", StringComparison.Ordinal);

    public void SetTarget(Transform newTarget)
    {
        target = newTarget != null ? newTarget : transform;
    }

    void OnEnable()
    {
        ZoneEventBus.OnZoneEvent += HandleZoneEvent;
        EoM_Events.OnDataReceived += HandlePhysiologicalData;
        EoM_Events.OnLoggingStateChanged += HandleLoggingStateChanged;
    }

    void OnDisable()
    {
        ZoneEventBus.OnZoneEvent -= HandleZoneEvent;
        EoM_Events.OnDataReceived -= HandlePhysiologicalData;
        EoM_Events.OnLoggingStateChanged -= HandleLoggingStateChanged;
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

        if (!isRecording || target == null)
        {
            return;
        }

        if (useSessionTime && !ExciteOMeterManager.currentlyRecordingSession)
        {
            return;
        }

        double elapsed = GetRecordingElapsedSeconds();
        if (elapsed < nextSampleTime)
        {
            return;
        }

        RecordSample(elapsed);
        nextSampleTime = elapsed + sampleInterval;
    }

    public void StartRecording()
    {
        isRecording = true;
        hasPreviousSample = false;
        nextSampleTime = useSessionTime ? 0.0 : Time.timeAsDouble;

        if (ShouldIncludePhysiologicalSignals)
        {
            ResetPhysiologicalSignals();
        }
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
        csv.Append("sample_index,");
        csv.Append(useSessionTime ? "session_time" : "elapsed_seconds");
        csv.Append(",local_time_iso8601,x,y,z,rotation_x,rotation_y,rotation_z,speed_mps,is_in_C,is_in_F,is_in_O");
        if (ShouldIncludePhysiologicalSignals)
        {
            csv.Append(",ECG,HR,RMSSD");
        }
        csv.AppendLine();

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

            if (ShouldIncludePhysiologicalSignals)
            {
                csv.Append(',');
                AppendSignalValue(csv, sample.HasEcg, sample.Ecg);
                csv.Append(',');
                AppendSignalValue(csv, sample.HasHr, sample.Hr);
                csv.Append(',');
                AppendSignalValue(csv, sample.HasRmssd, sample.Rmssd);
            }
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
        csv.Append(useSessionTime ? "session_time" : "elapsed_seconds");
        csv.AppendLine(",local_time_iso8601,zone,event_type");

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

        if (useSessionTime && ExciteOMeterManager.currentlyRecordingSession)
        {
            zoneEvent.ElapsedSeconds = (float)ExciteOMeterManager.GetTimestampDouble();
        }

        zoneEvents.Add(zoneEvent);
    }

    void HandlePhysiologicalData(DataType type, float timestamp, float value)
    {
        if (!ShouldIncludePhysiologicalSignals || !isRecording || !ExciteOMeterManager.currentlyRecordingSession)
        {
            return;
        }

        if (useSessionTime)
        {
            switch (type)
            {
                case DataType.RawECG:
                    ecgQueue.Enqueue(new TimedSignalValue(timestamp, value));
                    break;
                case DataType.HeartRate:
                    hrQueue.Enqueue(new TimedSignalValue(timestamp, value));
                    break;
                case DataType.RMSSD:
                    rmssdQueue.Enqueue(new TimedSignalValue(timestamp, value));
                    break;
            }

            return;
        }

        switch (type)
        {
            case DataType.RawECG:
                latestEcg = value;
                hasEcg = true;
                break;
            case DataType.HeartRate:
                latestHr = value;
                hasHr = true;
                break;
            case DataType.RMSSD:
                latestRmssd = value;
                hasRmssd = true;
                break;
        }
    }

    void HandleLoggingStateChanged(bool isLogging)
    {
        if (!isLogging || !ShouldIncludePhysiologicalSignals)
        {
            return;
        }

        // Values received while AutoRecorder was waiting only prove that the
        // streams are ready. A new Path recording must not reuse those values.
        if (useSessionTime)
        {
            nextSampleTime = 0.0;
        }

        hasPreviousSample = false;
        ResetPhysiologicalSignals();
    }

    double GetRecordingElapsedSeconds()
    {
        return useSessionTime
            ? ExciteOMeterManager.GetTimestampDouble()
            : Time.timeAsDouble;
    }

    static void AppendSignalValue(StringBuilder csv, bool hasValue, float value)
    {
        if (hasValue)
        {
            csv.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }
    }

    void RecordSample(double elapsed)
    {
        if (useSessionTime && ShouldIncludePhysiologicalSignals)
        {
            AdvancePhysiologicalSignals(elapsed);
        }

        Vector3 position = target.position;
        float speedMps = 0f;

        if (hasPreviousSample)
        {
            Vector3 delta = position - lastSamplePosition;
            delta.y = 0f;
            double deltaTime = elapsed - lastSampleElapsed;
            if (deltaTime > 0f)
            {
                speedMps = (float)(delta.magnitude / deltaTime);
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
            IsInO = ZoneEventBus.InO,
            Ecg = latestEcg,
            Hr = latestHr,
            Rmssd = latestRmssd,
            HasEcg = hasEcg,
            HasHr = hasHr,
            HasRmssd = hasRmssd
        });

        lastSamplePosition = position;
        lastSampleElapsed = elapsed;
        hasPreviousSample = true;
    }

    struct PathSample
    {
        public double ElapsedSeconds;
        public string LocalTimeIso8601;
        public Vector3 Position;
        public Quaternion Rotation;
        public float SpeedMps;
        public bool IsInC;
        public bool IsInF;
        public bool IsInO;
        public float Ecg;
        public float Hr;
        public float Rmssd;
        public bool HasEcg;
        public bool HasHr;
        public bool HasRmssd;
    }

    readonly struct TimedSignalValue
    {
        public TimedSignalValue(float timestamp, float value)
        {
            Timestamp = timestamp;
            Value = value;
        }

        public float Timestamp { get; }
        public float Value { get; }
    }

    void ResetPhysiologicalSignals()
    {
        ecgQueue.Clear();
        hrQueue.Clear();
        rmssdQueue.Clear();
        hasEcg = false;
        hasHr = false;
        hasRmssd = false;
    }

    void AdvancePhysiologicalSignals(double elapsed)
    {
        AdvanceSignalQueue(ecgQueue, elapsed, ref latestEcg, ref hasEcg);
        AdvanceSignalQueue(hrQueue, elapsed, ref latestHr, ref hasHr);
        AdvanceSignalQueue(rmssdQueue, elapsed, ref latestRmssd, ref hasRmssd);
    }

    static void AdvanceSignalQueue(
        Queue<TimedSignalValue> queue,
        double elapsed,
        ref float latestValue,
        ref bool hasValue)
    {
        const double TimestampToleranceSeconds = 0.001;
        while (queue.Count > 0
            && queue.Peek().Timestamp <= elapsed + TimestampToleranceSeconds)
        {
            TimedSignalValue sample = queue.Dequeue();
            latestValue = sample.Value;
            hasValue = true;
        }
    }
}
