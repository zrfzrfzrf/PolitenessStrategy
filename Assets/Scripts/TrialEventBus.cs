using System;

public static class TrialEventBus
{
    public static event Action<string> OnTrialEvent;

    public static void Report(string eventLabel)
    {
        if (string.IsNullOrEmpty(eventLabel))
        {
            return;
        }

        OnTrialEvent?.Invoke(eventLabel);
    }

    public static void ReportTrialStart(int trialNumber)
    {
        Report(trialNumber <= 1 ? "start" : trialNumber.ToString());
    }

    public static void ReportSessionEnd()
    {
        Report("end");
    }
}
