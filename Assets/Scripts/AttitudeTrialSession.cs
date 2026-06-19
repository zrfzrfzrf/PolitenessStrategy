using System;
using UnityEngine;

public static class AttitudeTrialSession
{
    const int TrialCount = 9;

    public readonly struct TrialDefinition
    {
        public TrialDefinition(CamilaAttitudeController.Attitude initialAttitude,
            CamilaAttitudeController.Attitude enterCAttitude)
        {
            InitialAttitude = initialAttitude;
            EnterCAttitude = enterCAttitude;
        }

        public CamilaAttitudeController.Attitude InitialAttitude { get; }
        public CamilaAttitudeController.Attitude EnterCAttitude { get; }
    }

    public static int TotalTrials => TrialCount;

    public static bool HasActiveSession
    {
        get
        {
            EnsureLoaded();
            return shuffledOrder != null;
        }
    }

    public static bool IsSessionComplete
    {
        get
        {
            EnsureLoaded();
            return currentIndex >= TrialCount;
        }
    }

    public static int CompletedTrialCount
    {
        get
        {
            EnsureLoaded();
            return Mathf.Min(currentIndex, TrialCount);
        }
    }

    public static int CurrentTrialNumber
    {
        get
        {
            EnsureLoaded();
            return IsSessionComplete ? TrialCount : currentIndex + 1;
        }
    }

    static int currentIndex;
    static int[] shuffledOrder;
    static bool isLoaded;

    public static void StartNewSession()
    {
        shuffledOrder = CreateShuffledOrder();
        currentIndex = 0;
        isLoaded = true;
    }

    public static void ResetSession()
    {
        shuffledOrder = null;
        currentIndex = 0;
        isLoaded = false;
    }

    public static TrialDefinition GetCurrentTrial()
    {
        EnsureLoaded();

        if (IsSessionComplete)
        {
            throw new InvalidOperationException("All attitude trials have already been completed.");
        }

        return DecodeTrialId(shuffledOrder[currentIndex]);
    }

    public static void CompleteCurrentTrial()
    {
        EnsureLoaded();

        if (IsSessionComplete)
        {
            return;
        }

        currentIndex++;
    }

    public static string GetProgressLabel()
    {
        EnsureLoaded();
        return $"{CurrentTrialNumber}/{TrialCount}";
    }

    static void EnsureLoaded()
    {
        if (isLoaded)
        {
            return;
        }

        StartNewSession();
    }

    static int[] CreateShuffledOrder()
    {
        int[] order = new int[TrialCount];
        for (int i = 0; i < TrialCount; i++)
        {
            order[i] = i;
        }

        for (int i = order.Length - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            int temp = order[i];
            order[i] = order[swapIndex];
            order[swapIndex] = temp;
        }

        return order;
    }

    public static TrialDefinition DecodeTrialId(int trialId)
    {
        if (trialId < 0 || trialId >= TrialCount)
        {
            throw new ArgumentOutOfRangeException(nameof(trialId), trialId, "Trial id must be between 0 and 8.");
        }

        return new TrialDefinition(
            (CamilaAttitudeController.Attitude)(trialId / 3),
            (CamilaAttitudeController.Attitude)(trialId % 3));
    }
}
