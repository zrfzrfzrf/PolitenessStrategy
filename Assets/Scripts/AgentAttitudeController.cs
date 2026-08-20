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

    public enum FResponseStrategy
    {
        WelcomeBack,
        WaitingForYou
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
    [SerializeField] StrategyPlan strategyPlan;
    [SerializeField] bool enableResponses = true;
    [SerializeField] AttitudeStateEvent onStateChanged;

    const int DefaultTrialCount = 9;

    int[] trialOrder = new int[0];

    Attitude currentAttitude;
    Attitude currentTrialEnterCAttitude;
    FResponseStrategy currentDirectFStrategy;
    FResponseStrategy currentIndirectFStrategy;
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
    public bool IsWaitingForNextTrial => waitingForNextTrial;
    public int TotalTrials => GetTrialCount();
    public int CompletedTrialCount => Mathf.Min(currentTrialIndex, TotalTrials);
    public int CurrentTrialNumber => isTerminal && currentTrialIndex >= TotalTrials
        ? TotalTrials
        : Mathf.Min(currentTrialIndex + 1, TotalTrials);

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
            Debug.Log($"All {TotalTrials} attitude trials are complete.");
            return;
        }

        GetTrialConfig(
            currentTrialIndex,
            out currentAttitude,
            out currentTrialEnterCAttitude,
            out currentDirectFStrategy,
            out currentIndirectFStrategy);
        currentPhase = Phase.Invitation;
        isPlayerInC = false;
        isTerminal = false;
        isTrialCompleting = false;

        Debug.Log(
            $"Starting trial {GetProgressLabel()}: " +
            $"Invitation={currentAttitude}, EnterC={currentTrialEnterCAttitude}, " +
            $"DirectF={currentDirectFStrategy}, IndirectF={currentIndirectFStrategy}");

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
        PlayFResponse(skippedEnterC);
        CompleteTrialAndAdvance("Praise");
    }

    void StartNewSession()
    {
        int trialCount = GetTrialCount();
        trialOrder = new int[trialCount];

        for (int i = 0; i < trialCount; i++)
        {
            trialOrder[i] = i;
        }

        if (ShouldShuffleTrials())
        {
            for (int i = trialOrder.Length - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                int temp = trialOrder[i];
                trialOrder[i] = trialOrder[swapIndex];
                trialOrder[swapIndex] = temp;
            }
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
        return currentTrialIndex >= TotalTrials;
    }

    string GetProgressLabel()
    {
        return $"{CurrentTrialNumber}/{TotalTrials}";
    }

    int GetTrialCount()
    {
        return HasStrategyPlan() ? strategyPlan.TrialCount : DefaultTrialCount;
    }

    bool HasStrategyPlan()
    {
        return strategyPlan != null && strategyPlan.TrialCount > 0;
    }

    bool ShouldShuffleTrials()
    {
        return !HasStrategyPlan() || strategyPlan.ShuffleOnStart;
    }

    void GetTrialConfig(
        int trialIndex,
        out Attitude initialAttitude,
        out Attitude enterCAttitude,
        out FResponseStrategy directFStrategy,
        out FResponseStrategy indirectFStrategy)
    {
        int orderedTrialIndex = trialOrder[trialIndex];
        directFStrategy = FResponseStrategy.WelcomeBack;
        indirectFStrategy = FResponseStrategy.WaitingForYou;

        if (HasStrategyPlan())
        {
            StrategyTrial trial = strategyPlan.Trials[orderedTrialIndex];
            if (trial == null)
            {
                Debug.LogWarning($"StrategyPlan {strategyPlan.name} has an empty trial at index {orderedTrialIndex}. Using Polite -> Polite with default F strategies.");
                initialAttitude = Attitude.Polite;
                enterCAttitude = Attitude.Polite;
                return;
            }

            initialAttitude = trial.invitationAttitude;
            enterCAttitude = trial.enterCAttitude;
            directFStrategy = trial.directFStrategy;
            indirectFStrategy = trial.indirectFStrategy;
            return;
        }

        DecodeDefaultTrialId(orderedTrialIndex, out initialAttitude, out enterCAttitude);
    }

    static void DecodeDefaultTrialId(int trialId, out Attitude initialAttitude, out Attitude enterCAttitude)
    {
        if (trialId < 0 || trialId >= DefaultTrialCount)
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

        Debug.Log($"Trial completed with {outcome}. Finished {CompletedTrialCount}/{TotalTrials}.");

        if (IsSessionComplete())
        {
            Debug.Log($"All {TotalTrials} attitude trials are complete.");
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

    void PlayFResponse(bool skippedEnterC)
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

        FResponseStrategy strategy = skippedEnterC
            ? currentDirectFStrategy
            : currentIndirectFStrategy;

        string stateName = GetFStateName(strategy, profile);
        if (string.IsNullOrEmpty(stateName))
        {
            return;
        }

        animator.CrossFade(Animator.StringToHash(stateName), profile.CrossFadeSeconds, 0, 0f);
        Debug.Log($"{GetAgentName()} playing F strategy: {strategy} -> {stateName} ({(skippedEnterC ? "Direct F" : "Indirect F")})");
    }

    static string GetFStateName(FResponseStrategy strategy, AgentAnimationProfile profile)
    {
        switch (strategy)
        {
            case FResponseStrategy.WelcomeBack:
                return string.IsNullOrEmpty(profile.DirectFState)
                    ? profile.PraiseState
                    : profile.DirectFState;
            case FResponseStrategy.WaitingForYou:
                return string.IsNullOrEmpty(profile.IndirectFState)
                    ? profile.PraiseState
                    : profile.IndirectFState;
            default:
                return profile.PraiseState;
        }
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
