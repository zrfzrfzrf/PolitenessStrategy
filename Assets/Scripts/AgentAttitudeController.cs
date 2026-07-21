using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class AgentAttitudeController : MonoBehaviour
{
    public enum Attitude
    {
        Polite,
        Neutral,
        Hostile
    }

    public enum Phase
    {
        Invitation,
        RequestToMove,
        Praise,
        Ignore
    }

    [Serializable]
    public class AttitudeState
    {
        public Attitude attitude;
        public Phase phase;
    }

    [Serializable]
    public class AttitudeStateEvent : UnityEvent<AttitudeState> { }

    [SerializeField, Min(0.1f)] float stayAtCTimeoutSeconds = 8f;
    [SerializeField] AgentManager agentManager;
    [SerializeField] bool enableResponses = true;
    [SerializeField] AttitudeStateEvent onStateChanged;

    const int TrialCount = 9;

    readonly int[] trialOrder = new int[TrialCount];

    Attitude currentAttitude;
    Attitude currentTrialEnterCAttitude;
    Phase currentPhase;
    bool isPlayerInC;
    bool isTerminal;
    bool isTrialCompleting;
    bool waitingForNextTrial;
    bool sessionStarted;
    int currentTrialIndex;
    Coroutine stayAtCCoroutine;

    public Attitude CurrentAttitude => currentAttitude;
    public Phase CurrentPhase => currentPhase;
    public bool IsTerminal => isTerminal;
    public bool IsPlayerInC => isPlayerInC;
    public int TotalTrials => TrialCount;
    public int CompletedTrialCount => Mathf.Min(currentTrialIndex, TrialCount);
    public int CurrentTrialNumber => isTerminal && currentTrialIndex >= TrialCount
        ? TrialCount
        : Mathf.Min(currentTrialIndex + 1, TrialCount);

    void Awake()
    {
        if (agentManager == null)
        {
            agentManager = GetComponent<AgentManager>();
        }

        if (agentManager != null)
        {
            agentManager.OnAgentChanged += HandleAgentChanged;
        }

        StartNewSession();
        BeginCurrentTrial();
    }

    void OnDestroy()
    {
        if (agentManager != null)
        {
            agentManager.OnAgentChanged -= HandleAgentChanged;
        }
    }

    public void OnNextTrialClicked()
    {
        if (!waitingForNextTrial || IsSessionComplete())
        {
            return;
        }

        waitingForNextTrial = false;
        BeginCurrentTrial();
    }

    public void OnRestartClicked()
    {
        CancelStayAtCTimer();
        StartNewSession();
        waitingForNextTrial = false;
        BeginCurrentTrial();
    }

    public void BeginCurrentTrial()
    {
        EnsureSessionStarted();
        CancelStayAtCTimer();

        if (IsSessionComplete())
        {
            isTerminal = true;
            currentPhase = Phase.Praise;
            Debug.Log("All 9 attitude trials are complete.");
            return;
        }

        DecodeTrialId(trialOrder[currentTrialIndex], out currentAttitude, out currentTrialEnterCAttitude);
        currentPhase = Phase.Invitation;
        isPlayerInC = false;
        isTerminal = false;
        isTrialCompleting = false;

        Debug.Log(
            $"Starting trial {GetProgressLabel()}: " +
            $"Invitation={currentAttitude}, EnterC={currentTrialEnterCAttitude}");

        ResetAnimatorForNewTrial();
        NotifyStateChanged("Trial started");
        PlayResponse();
    }

    public void OnPlayerEnterC()
    {
        if (isTerminal)
        {
            return;
        }

        isPlayerInC = true;

        if (currentPhase == Phase.Invitation)
        {
            Attitude invitationAttitude = currentAttitude;
            currentAttitude = currentTrialEnterCAttitude;
            currentPhase = Phase.RequestToMove;
            NotifyStateChanged(
                $"Entered C (Invitation {invitationAttitude} -> RequestToMove {currentAttitude})");
            PlayResponse();
        }

        RestartStayAtCTimer();
    }

    public void OnPlayerExitC()
    {
        isPlayerInC = false;
        CancelStayAtCTimer();
    }

    public void OnPlayerEnterF()
    {
        if (isTerminal)
        {
            return;
        }

        CancelStayAtCTimer();

        bool skippedEnterC = currentPhase == Phase.Invitation;
        currentAttitude = Attitude.Polite;
        currentPhase = Phase.Praise;
        isTerminal = true;

        NotifyStateChanged(skippedEnterC
            ? "Entered F directly; EnterC strategy skipped"
            : "Entered F");
        PlayTerminalResponse();
        CompleteTrialAndAdvance("Praise");
    }

    void StartNewSession()
    {
        for (int i = 0; i < TrialCount; i++)
        {
            trialOrder[i] = i;
        }

        for (int i = trialOrder.Length - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            int temp = trialOrder[i];
            trialOrder[i] = trialOrder[swapIndex];
            trialOrder[swapIndex] = temp;
        }

        currentTrialIndex = 0;
        sessionStarted = true;
    }

    void EnsureSessionStarted()
    {
        if (!sessionStarted)
        {
            StartNewSession();
        }
    }

    bool IsSessionComplete()
    {
        return currentTrialIndex >= TrialCount;
    }

    string GetProgressLabel()
    {
        return $"{CurrentTrialNumber}/{TrialCount}";
    }

    static void DecodeTrialId(int trialId, out Attitude initialAttitude, out Attitude enterCAttitude)
    {
        if (trialId < 0 || trialId >= TrialCount)
        {
            throw new ArgumentOutOfRangeException(nameof(trialId), trialId, "Trial id must be between 0 and 8.");
        }

        initialAttitude = (Attitude)(trialId / 3);
        enterCAttitude = (Attitude)(trialId % 3);
    }

    void RestartStayAtCTimer()
    {
        CancelStayAtCTimer();

        if (currentPhase != Phase.RequestToMove || isTerminal)
        {
            return;
        }

        stayAtCCoroutine = StartCoroutine(StayAtCTimeoutRoutine());
    }

    IEnumerator StayAtCTimeoutRoutine()
    {
        yield return new WaitForSeconds(stayAtCTimeoutSeconds);

        if (isTerminal || currentPhase != Phase.RequestToMove || !isPlayerInC)
        {
            yield break;
        }

        currentAttitude = Attitude.Neutral;
        currentPhase = Phase.Ignore;
        isTerminal = true;

        NotifyStateChanged("Stayed at C");
        PlayTerminalResponse();
        CompleteTrialAndAdvance("Ignore");
    }

    void CompleteTrialAndAdvance(string outcome)
    {
        if (isTrialCompleting)
        {
            return;
        }

        isTrialCompleting = true;
        currentTrialIndex++;

        Debug.Log($"Trial completed with {outcome}. Finished {CompletedTrialCount}/{TrialCount}.");

        if (IsSessionComplete())
        {
            Debug.Log("All 9 attitude trials are complete.");
            return;
        }

        waitingForNextTrial = true;
        Debug.Log("Trial finished. Walk back, then click next-trial.");
    }

    void CancelStayAtCTimer()
    {
        if (stayAtCCoroutine == null)
        {
            return;
        }

        StopCoroutine(stayAtCCoroutine);
        stayAtCCoroutine = null;
    }

    void NotifyStateChanged(string reason = null)
    {
        string agentName = GetAgentName();
        string message = string.IsNullOrEmpty(reason)
            ? $"{agentName} state: {currentPhase} / {currentAttitude}"
            : $"{agentName} state: {currentPhase} / {currentAttitude} ({reason})";
        Debug.Log(message);

        if (onStateChanged == null)
        {
            return;
        }

        onStateChanged.Invoke(new AttitudeState
        {
            attitude = currentAttitude,
            phase = currentPhase
        });
    }

    void PlayResponse()
    {
        if (!enableResponses)
        {
            return;
        }

        if (currentPhase != Phase.Invitation && currentPhase != Phase.RequestToMove)
        {
            return;
        }

        Animator animator = GetAnimator();
        AgentAnimationProfile profile = GetAnimationProfile();

        if (animator == null)
        {
            Debug.LogWarning("AgentAttitudeController has no Animator available.");
            return;
        }

        if (profile == null)
        {
            Debug.LogWarning("AgentAttitudeController has no AgentAnimationProfile available.");
            return;
        }

        string stateName = profile.GetStateName(currentAttitude);
        if (string.IsNullOrEmpty(stateName))
        {
            Debug.LogWarning($"No animation state configured for {currentAttitude}.");
            return;
        }

        int stateHash = Animator.StringToHash(stateName);

        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            PlayIdle(animator, profile);
            animator.Update(0f);
        }

        animator.CrossFade(stateHash, profile.CrossFadeSeconds, 0, 0f);
        Debug.Log($"{GetAgentName()} playing animation: {stateName} ({currentPhase} / {currentAttitude})");
    }

    void PlayTerminalResponse()
    {
        if (!enableResponses)
        {
            return;
        }

        Animator animator = GetAnimator();
        AgentAnimationProfile profile = GetAnimationProfile();

        if (animator == null || profile == null)
        {
            return;
        }

        string stateName = currentPhase == Phase.Praise
            ? profile.PraiseState
            : profile.IgnoreState;

        if (string.IsNullOrEmpty(stateName))
        {
            return;
        }

        animator.CrossFade(Animator.StringToHash(stateName), profile.CrossFadeSeconds, 0, 0f);
        Debug.Log($"{GetAgentName()} playing animation: {stateName} ({currentPhase} / {currentAttitude})");
    }

    void ResetAnimatorForNewTrial()
    {
        Animator animator = GetAnimator();
        AgentAnimationProfile profile = GetAnimationProfile();

        if (animator == null || profile == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
        PlayIdle(animator, profile);
        animator.Update(0f);
    }

    void PlayIdle(Animator animator, AgentAnimationProfile profile)
    {
        if (string.IsNullOrEmpty(profile.IdleState))
        {
            return;
        }

        animator.Play(profile.IdleState, 0, 0f);
    }

    void HandleAgentChanged()
    {
        ResetAnimatorForNewTrial();
    }

    Animator GetAnimator()
    {
        return agentManager != null ? agentManager.CurrentAnimator : null;
    }

    AgentAnimationProfile GetAnimationProfile()
    {
        return agentManager != null ? agentManager.CurrentAnimationProfile : null;
    }

    string GetAgentName()
    {
        AgentDefinition agent = agentManager != null ? agentManager.CurrentAgent : null;
        return agent != null ? agent.AgentId : "Agent";
    }
}
