using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using ExciteOMeter;

public class ExampleNewInputSystemAutoStart : MonoBehaviour
{
    const string TargetSceneName = "Example_withURP_NewInputSystem";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != TargetSceneName)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("ExampleNewInputSystemAutoStart");
        bootstrapObject.AddComponent<ExampleNewInputSystemAutoStart>();
        bootstrapObject.AddComponent<PhysioCombinedCsvLogger>();
        CreateEcgInlet("LSL_RawECG_Auto", "RawECG");
        CreateEcgInlet("LSL_ECG_Auto", "ECG");
    }

    static void CreateEcgInlet(string objectName, string streamName)
    {
        GameObject inletObject = new GameObject(objectName);
        inletObject.SetActive(false);

        LSL_Inlet_ECG ecgInlet = inletObject.AddComponent<LSL_Inlet_ECG>();
        ecgInlet.StreamName = streamName;
        ecgInlet.StreamType = "ExciteOMeter";
        ecgInlet.Devices = Devices.PolarH10;
        ecgInlet.VariableType = DataType.RawECG;

        inletObject.SetActive(true);
    }

    IEnumerator Start()
    {
        while (LoggerController.instance == null || !LoggerController.instance.IsReady)
        {
            yield return null;
        }

        LoggerController.instance.StartLogSession();
    }
}

public class PhysioCombinedCsvLogger : MonoBehaviour
{
    const string Filename = "physio.csv";
    const float OutputSamplePeriod = 1.0f;

    class PhysioRow
    {
        public float sessionTime;
        public string datetime;
        public float? ecg;
        public float? hr;
        public float? rmssd;
    }

    readonly Dictionary<int, PhysioRow> rowsBySample = new Dictionary<int, PhysioRow>();
    string filepath;
    bool isLogging;

    void OnEnable()
    {
        EoM_Events.OnLoggingStateChanged += OnLoggingStateChanged;
        EoM_Events.OnDataReceived += OnDataReceived;
    }

    void OnDisable()
    {
        EoM_Events.OnLoggingStateChanged -= OnLoggingStateChanged;
        EoM_Events.OnDataReceived -= OnDataReceived;
        WriteAndClear();
    }

    void OnLoggingStateChanged(bool isLogging)
    {
        if (isLogging)
        {
            StartNewFile();
        }
        else
        {
            WriteAndClear();
        }
    }

    void OnDataReceived(DataType type, float timestamp, float value)
    {
        if (!isLogging)
        {
            return;
        }

        int key = Mathf.Max(0, Mathf.RoundToInt(timestamp / OutputSamplePeriod));
        if (!rowsBySample.TryGetValue(key, out PhysioRow row))
        {
            row = new PhysioRow
            {
                sessionTime = key * OutputSamplePeriod,
                datetime = DataLogger.GetTimestamp(System.DateTime.Now)
            };
            rowsBySample.Add(key, row);
        }

        switch (type)
        {
            case DataType.RawECG:
                row.ecg = value;
                break;
            case DataType.HeartRate:
                row.hr = value;
                break;
            case DataType.RMSSD:
                row.rmssd = value;
                break;
            default:
                return;
        }
    }

    void StartNewFile()
    {
        WriteAndClear();

        string sessionPath = LoggerController.instance.CurrentSessionLogPath;
        if (string.IsNullOrEmpty(sessionPath))
        {
            Debug.LogWarning("Cannot create physio.csv because the session log path is not ready.");
            return;
        }

        filepath = Path.Combine(sessionPath, Filename);
        rowsBySample.Clear();
        isLogging = true;
    }

    void WriteAndClear()
    {
        if (!isLogging || string.IsNullOrEmpty(filepath))
        {
            return;
        }

        List<PhysioRow> rows = rowsBySample.Values
            .OrderBy(row => row.sessionTime)
            .ToList();

        BackfillMissingValues(rows);

        using (StreamWriter writer = new StreamWriter(filepath, false))
        {
            writer.WriteLine("datetime,sessionTime,ECG,HR,RMSSD");
            foreach (PhysioRow row in rows)
            {
                writer.WriteLine(
                    row.datetime + "," +
                    Format(row.sessionTime) + "," +
                    Format(row.ecg) + "," +
                    Format(row.hr) + "," +
                    Format(row.rmssd));
            }
        }

        rowsBySample.Clear();
        filepath = null;
        isLogging = false;
    }

    static void BackfillMissingValues(List<PhysioRow> rows)
    {
        float? latestEcg = null;
        float? latestHr = null;
        float? latestRmssd = null;

        foreach (PhysioRow row in rows)
        {
            if (row.ecg.HasValue) latestEcg = row.ecg;
            else row.ecg = latestEcg;

            if (row.hr.HasValue) latestHr = row.hr;
            else row.hr = latestHr;

            if (row.rmssd.HasValue) latestRmssd = row.rmssd;
            else row.rmssd = latestRmssd;
        }

        float? firstEcg = rows.FirstOrDefault(row => row.ecg.HasValue)?.ecg;
        float? firstHr = rows.FirstOrDefault(row => row.hr.HasValue)?.hr;
        float? firstRmssd = rows.FirstOrDefault(row => row.rmssd.HasValue)?.rmssd;

        foreach (PhysioRow row in rows)
        {
            if (!row.ecg.HasValue) row.ecg = firstEcg;
            if (!row.hr.HasValue) row.hr = firstHr;
            if (!row.rmssd.HasValue) row.rmssd = firstRmssd;
        }
    }

    static string Format(float? value)
    {
        return value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "";
    }

    static string Format(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}