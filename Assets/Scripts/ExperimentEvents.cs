using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static class ZoneEventBus
{
    public static bool InC { get; private set; }
    public static bool InF { get; private set; }
    public static bool InO { get; private set; }

    public static event Action<ZoneEvent> OnZoneEvent;

    public static void Report(string zone, string eventType)
    {
        if (eventType == "enter")
        {
            SetInside(zone, true);
        }
        else if (eventType == "exit")
        {
            SetInside(zone, false);
        }

        OnZoneEvent?.Invoke(new ZoneEvent
        {
            ElapsedSeconds = Time.time,
            LocalTimeIso8601 = ResultsWriter.Now,
            Zone = zone,
            EventType = eventType
        });
    }

    static void SetInside(string zone, bool inside)
    {
        switch (zone)
        {
            case "C":
                InC = inside;
                break;
            case "F":
                InF = inside;
                break;
            case "O":
                InO = inside;
                break;
        }
    }
}

public struct ZoneEvent
{
    public float ElapsedSeconds;
    public string LocalTimeIso8601;
    public Vector3 Position;
    public bool HasPosition;
    public string Zone;
    public string EventType;
}

// Labels written to the event column of the path CSV:
// R1T1 (trial start), R1T1_Q / R1T1_Qend (trial questionnaire), R1_TLX / R1_TLXend, R1_rest, end.
public static class TrialEventBus
{
    public static event Action<string> OnTrialEvent;

    public static void Report(string eventLabel) => OnTrialEvent?.Invoke(eventLabel);
}

public class TrialRecord
{
    public int Round, Trial;
    public string Invitation, EnterC, DirectF, IndirectF, Outcome, Route, StartTime, EndTime;
    public int[] Answers = new int[QuestionnaireData.Perception.Length];
}

// Every run writes into one folder: {RootDir}/{participant}_{timestamp}/
// trials.csv (one row per trial, answers 1-9, blank = not asked) and rounds.csv (NASA-TLX per round).
public static class ResultsWriter
{
    public static string ParticipantId = "Participant";

#if UNITY_ANDROID && !UNITY_EDITOR
    public static string RootDir => Application.persistentDataPath;
#else
    public static string RootDir => @"D:\KTH\SummerIntern\Dataset";
#endif

    static string sessionDir;

    public static string SessionDir
    {
        get
        {
            if (sessionDir == null)
            {
                sessionDir = Path.Combine(RootDir, $"{ParticipantId}_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(sessionDir);
            }

            return sessionDir;
        }
    }

    public static string Now => DateTime.Now.ToString("O", CultureInfo.InvariantCulture);

    public static void NewSession() => sessionDir = null;

    public static void WriteTrial(TrialRecord r)
    {
        string header = "participant,round,trial,invitation,enterC,directF,indirectF,outcome,route,start_time,end_time,"
            + string.Join(",", QuestionnaireData.Perception.Select((q, i) => $"Q{i + 1}_{q.Id}"));
        string row = string.Join(",", new object[]
        {
            ParticipantId, r.Round, r.Trial, r.Invitation, r.EnterC, r.DirectF, r.IndirectF,
            r.Outcome, r.Route, r.StartTime, r.EndTime
        }.Concat(r.Answers.Select(a => a == 0 ? "" : a.ToString())));
        Append("trials.csv", header, row);
    }

    public static void WriteRound(int round, int[] tlx)
    {
        string header = "participant,round,"
            + string.Join(",", QuestionnaireData.Tlx.Select((q, i) => $"TLX{i + 1}_{q.Id}")) + ",time";
        Append("rounds.csv", header, $"{ParticipantId},{round},{string.Join(",", tlx)},{Now}");
    }

    static void Append(string file, string header, string row)
    {
        string path = Path.Combine(SessionDir, file);
        if (!File.Exists(path))
        {
            File.AppendAllText(path, header + "\n");
        }

        File.AppendAllText(path, row + "\n");
    }
}
