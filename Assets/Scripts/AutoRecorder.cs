using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ExciteOMeter;

[DefaultExecutionOrder(1000)]
public sealed class AutoRecorder : MonoBehaviour
{
    [Header("Runtime and clock")]
    [SerializeField] private bool forceRunInBackground = true;
    [SerializeField] private bool useMonotonicSessionClock = true;

    [Header("Automatic session")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private string sessionId = "Participant";
    [SerializeField] private bool waitForRequiredStreams = true;
    [SerializeField] private bool waitForFirstSamples = true;
    [SerializeField, Min(0f)] private float streamWaitTimeoutSeconds;

    private static readonly DataType[] RequiredStreams =
    {
        DataType.RawECG,
        DataType.HeartRate,
        DataType.RRInterval,
    };

    private readonly HashSet<DataType> connectedStreams = new HashSet<DataType>();
    private readonly HashSet<DataType> streamsWithSamples = new HashSet<DataType>();

    private void Awake()
    {
        if (forceRunInBackground)
        {
            Application.runInBackground = true;
        }

        ExciteOMeterManager.ConfigureSessionClock(useMonotonicSessionClock);
    }

    private void OnEnable()
    {
        EoM_Events.OnStreamConnected += HandleStreamConnected;
        EoM_Events.OnStreamDisconnected += HandleStreamDisconnected;
        EoM_Events.OnDataReceived += HandleDataReceived;
    }

    private void OnDisable()
    {
        EoM_Events.OnStreamConnected -= HandleStreamConnected;
        EoM_Events.OnStreamDisconnected -= HandleStreamDisconnected;
        EoM_Events.OnDataReceived -= HandleDataReceived;
    }

    private IEnumerator Start()
    {
        yield return null;

        float initializationDeadline = Time.realtimeSinceStartup + 5f;

        while (!IsReady() && Time.realtimeSinceStartup < initializationDeadline)
        {
            yield return null;
        }

        if (!IsReady())
        {
            Debug.LogError("EOM initialization failed: required components are missing.");
            yield break;
        }

        SettingsManager.instance.SetSessionId(sessionId);

        if (!autoStart || ExciteOMeterManager.currentlyRecordingSession)
        {
            yield break;
        }

        if (waitForRequiredStreams)
        {
            float streamDeadline = streamWaitTimeoutSeconds > 0f
                ? Time.realtimeSinceStartup + streamWaitTimeoutSeconds
                : float.PositiveInfinity;

            Debug.Log("EOM waiting for ECG, HR and RRi streams before starting the session.");

            while (!RequiredSignalsAreReady() && Time.realtimeSinceStartup < streamDeadline)
            {
                yield return null;
            }

            if (!RequiredSignalsAreReady())
            {
                Debug.LogError($"EOM session was not started. Still waiting for: {DescribeMissingSignals()}");
                yield break;
            }
        }

        Debug.Log("EOM ECG, HR and RRi are ready. Starting the recording session.");
        ExciteOMeterManager.instance.StartSessionLog();
    }

    public void StopAndFinalize()
    {
        if (ExciteOMeterManager.currentlyRecordingSession)
        {
            ExciteOMeterManager.instance.StopSessionLog();
        }
    }

    private void OnApplicationQuit()
    {
        StopAndFinalize();
    }

    private static bool IsReady()
    {
        return ExciteOMeterManager.instance != null
            && SettingsManager.instance != null
            && LoggerController.instance != null
            && SessionVariablesController.instance != null;
    }

    private void HandleStreamConnected(DataType type)
    {
        if (IsRequired(type))
        {
            connectedStreams.Add(type);
        }
    }

    private void HandleStreamDisconnected(DataType type)
    {
        if (!IsRequired(type))
        {
            return;
        }

        connectedStreams.Remove(type);
        streamsWithSamples.Remove(type);
    }

    private void HandleDataReceived(DataType type, float timestamp, float value)
    {
        if (!IsRequired(type))
        {
            return;
        }

        // Receiving a sample is stronger evidence than a possibly missed connection event.
        connectedStreams.Add(type);
        streamsWithSamples.Add(type);
    }

    private bool RequiredSignalsAreReady()
    {
        return RequiredStreams.All(type => connectedStreams.Contains(type))
            && (!waitForFirstSamples || RequiredStreams.All(type => streamsWithSamples.Contains(type)));
    }

    private string DescribeMissingSignals()
    {
        IEnumerable<string> missing = RequiredStreams
            .Where(type => !connectedStreams.Contains(type)
                || (waitForFirstSamples && !streamsWithSamples.Contains(type)))
            .Select(type => type.ToString());

        return string.Join(", ", missing);
    }

    private static bool IsRequired(DataType type)
    {
        return type == DataType.RawECG
            || type == DataType.HeartRate
            || type == DataType.RRInterval;
    }
}
