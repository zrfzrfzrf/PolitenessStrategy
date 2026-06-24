using System;
using System.Globalization;
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
            LocalTimeIso8601 = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
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
    public string Zone;
    public string EventType;
}
