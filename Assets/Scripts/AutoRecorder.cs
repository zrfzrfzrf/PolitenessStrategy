using System.Collections;
using UnityEngine;
using ExciteOMeter;

[DefaultExecutionOrder(1000)]
public sealed class AutoRecorder : MonoBehaviour
{
    [SerializeField] private bool autoStart = true;
    [SerializeField] private string sessionId = "Participant";

    private IEnumerator Start()
    {
        yield return null;

        float timeout = Time.realtimeSinceStartup + 5f;

        while (!IsReady() && Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

        if (!IsReady())
        {
            Debug.LogError("EOM initialization failed: required components are missing.");
            yield break;
        }

        SettingsManager.instance.SetSessionId(sessionId);

        if (autoStart && !ExciteOMeterManager.currentlyRecordingSession)
        {
            ExciteOMeterManager.instance.StartSessionLog();
        }
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
}