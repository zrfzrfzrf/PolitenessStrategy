using System;
using System.Collections;
using System.Linq;
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

    [SerializeField, Min(0.1f)] float stayAtCTimeoutSeconds = 7f;
    [SerializeField] AgentManager agentManager;
    [SerializeField] StrategyPlan strategyPlan;
    [SerializeField] bool enableResponses = true;
    [SerializeField] AttitudeStateEvent onStateChanged;
    [SerializeField, Min(1)] int roundCount = 3;
    [SerializeField, Tooltip("Ask agent perception questions 1-4 after every trial")] bool askPerceptionPart1 = true;
    [SerializeField, Tooltip("Ask agent perception questions 5-7 after every trial")] bool askPerceptionPart2;

    const int DefaultTrialCount = 9;

    // (title, questions, onDone) - answered by QuestionnaireUI; onDone gets answers 1-9.
    public event Action<string, QuestionnaireData.Question[], Action<int[]>> OnAskQuestions;

    int[] trialOrder = new int[0];

    Attitude currentAttitude;
    Attitude currentTrialEnterCAttitude;
    FResponseStrategy currentDirectFStrategy;
    FResponseStrategy currentIndirectFStrategy;
    Phase currentPhase;
    bool isPlayerInC;
    bool hasEnteredCThisTrial;
    bool hasLeftCAfterEnter;
    bool isTerminal;
    bool isTrialCompleting;
    bool waitingForNextTrial;
    bool waitingForNextRound;
    bool sessionStarted;
    bool recordTrialEvents;
    int currentTrialIndex;
    TrialRecord record;
    Coroutine stayAtCCoroutine;

    public Attitude CurrentAttitude => currentAttitude;
    public Phase CurrentPhase => currentPhase;
    public bool IsTerminal => isTerminal;
    public bool IsPlayerInC => isPlayerInC;
    public bool IsWaitingForNextTrial => waitingForNextTrial;
    public bool IsInRoundBreak => waitingForNextRound;
    public int TrialsPerRound => GetTrialCount();
    public int TotalTrials => TrialsPerRound * roundCount;
    public int CompletedTrialCount => Mathf.Min(currentTrialIndex, TotalTrials);
    public int CurrentRound => Mathf.Min(currentTrialIndex, TotalTrials - 1) / TrialsPerRound + 1;
    public int CurrentTrialInRound => Mathf.Min(currentTrialIndex, TotalTrials - 1) % TrialsPerRound + 1;

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

    public void OnNextRoundClicked()
    {
        if (!waitingForNextRound)
        {
            return;
        }

        waitingForNextRound = false;
        waitingForNextTrial = true;
    }

    // Operator override: skips the current trial without questionnaires or log rows.
    public void ForceNextTrial()
    {
        CancelStayAtCTimer();

        if (!waitingForNextTrial && !IsSessionComplete())
        {
            isTrialCompleting = true;
            currentTrialIndex++;
            Debug.Log($"Trial force-skipped. Finished {CompletedTrialCount}/{TotalTrials}.");
        }

        waitingForNextTrial = false;
        waitingForNextRound = false;

        if (IsSessionComplete())
        {
            isTerminal = true;
            currentPhase = Phase.Praise;
            Debug.Log($"All {TotalTrials} attitude trials are complete.");
            Mark("end");
            return;
        }

        BeginCurrentTrial();
    }

    public void OnRestartClicked()
    {
        // Debug.Log($"[AudioDebug] AgentAttitudeController.OnRestartClicked on '{name}' time={Time.time:F3} frame={Time.frameCount}");
        CancelStayAtCTimer();
        StartNewSession();
        waitingForNextTrial = false;
        waitingForNextRound = false;
        recordTrialEvents = true;
        ResultsWriter.NewSession();
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
            Mark("end");
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
        hasEnteredCThisTrial = false;
        hasLeftCAfterEnter = false;
        isTerminal = false;
        isTrialCompleting = false;

        Debug.Log(
            $"Starting trial {GetProgressLabel()}: " +
            $"Invitation={currentAttitude}, EnterC={currentTrialEnterCAttitude}, " +
            $"DirectF={currentDirectFStrategy}, IndirectF={currentIndirectFStrategy}");

        record = new TrialRecord
        {
            Round = CurrentRound,
            Trial = CurrentTrialInRound,
            Invitation = currentAttitude.ToString(),
            EnterC = currentTrialEnterCAttitude.ToString(),
            DirectF = currentDirectFStrategy.ToString(),
            IndirectF = currentIndirectFStrategy.ToString(),
            StartTime = ResultsWriter.Now
        };
        Mark($"R{record.Round}T{record.Trial}");
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

        // Left C earlier in this trial, then came back -> Ignore immediately.
        if (hasLeftCAfterEnter)
        {
            CancelStayAtCTimer();
            currentAttitude = Attitude.Neutral;
            currentPhase = Phase.Ignore;
            isTerminal = true;
            NotifyStateChanged("Returned to C after leaving");
            PlayTerminalResponse();
            CompleteTrialAndAdvance("Ignore", "return_to_C");
            return;
        }

        if (currentPhase == Phase.Invitation)
        {
            Attitude invitationAttitude = currentAttitude;
            currentAttitude = currentTrialEnterCAttitude;
            currentPhase = Phase.RequestToMove;
            NotifyStateChanged(
                $"Entered C (Invitation {invitationAttitude} -> RequestToMove {currentAttitude})");
            PlayResponse();
        }

        hasEnteredCThisTrial = true;
        RestartStayAtCTimer();
    }

    public void OnPlayerExitC()
    {
        if (hasEnteredCThisTrial && !isTerminal)
        {
            hasLeftCAfterEnter = true;
        }

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
        CompleteTrialAndAdvance("Praise", skippedEnterC ? "direct_F" : "via_C");
    }

    // trialOrder holds roundCount blocks of plan indices; each block is shuffled on its own.
    void StartNewSession()
    {
        int perRound = GetTrialCount();
        trialOrder = new int[perRound * roundCount];

        for (int i = 0; i < trialOrder.Length; i++)
        {
            trialOrder[i] = i % perRound;
        }

        if (ShouldShuffleTrials())
        {
            for (int start = 0; start < trialOrder.Length; start += perRound)
            {
                for (int i = perRound - 1; i > 0; i--)
                {
                    int swap = UnityEngine.Random.Range(0, i + 1);
                    (trialOrder[start + i], trialOrder[start + swap]) = (trialOrder[start + swap], trialOrder[start + i]);
                }
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

    public string GetProgressLabel()
    {
        return $"Round {CurrentRound}/{roundCount}, Trial {CurrentTrialInRound}/{TrialsPerRound}";
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
        CompleteTrialAndAdvance("Ignore", "stay_timeout");
    }

    // Trial over -> trial questionnaire -> (end of round: TLX -> rest until Next_round) -> walk back to start.
    void CompleteTrialAndAdvance(string outcome, string route)
    {
        if (isTrialCompleting)
        {
            return;
        }

        isTrialCompleting = true;
        record.Outcome = outcome;
        record.Route = route;
        record.EndTime = ResultsWriter.Now;
        currentTrialIndex++;

        Debug.Log($"Trial completed with {outcome} ({route}). Finished {CompletedTrialCount}/{TotalTrials}.");

        string label = $"R{record.Round}T{record.Trial}";
        int first = askPerceptionPart1 ? 0 : QuestionnaireData.Part1Count;
        int last = askPerceptionPart2 ? QuestionnaireData.Perception.Length : QuestionnaireData.Part1Count;
        var asked = QuestionnaireData.Perception.Skip(first).Take(last - first).ToArray();

        Mark(label + "_Q");
        OnAskQuestions($"Round {record.Round}, Trial {record.Trial}", asked, answers =>
        {
            Array.Copy(answers, 0, record.Answers, first, answers.Length);
            Mark(label + "_Qend");
            if (recordTrialEvents)
            {
                ResultsWriter.WriteTrial(record);
            }

            AfterTrialQuestions();
        });
    }

    void AfterTrialQuestions()
    {
        if (currentTrialIndex % TrialsPerRound != 0)
        {
            waitingForNextTrial = true;
            Debug.Log("Trial finished. Walk back, then click next-trial.");
            return;
        }

        int round = currentTrialIndex / TrialsPerRound;
        Mark($"R{round}_TLX");
        OnAskQuestions($"Round {round} complete", QuestionnaireData.Tlx, answers =>
        {
            Mark($"R{round}_TLXend");
            if (recordTrialEvents)
            {
                ResultsWriter.WriteRound(round, answers);
            }

            if (IsSessionComplete())
            {
                Debug.Log($"All {TotalTrials} attitude trials are complete.");
                Mark("end");
                return;
            }

            waitingForNextRound = true;
            Mark($"R{round}_rest");
        });
    }

    void Mark(string label)
    {
        if (recordTrialEvents)
        {
            TrialEventBus.Report(label);
        }
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
            Debug.Log($"[AudioDebug] PlayResponse skipped because responses are disabled on '{name}'.");
            return;
        }

        if (currentPhase != Phase.Invitation && currentPhase != Phase.RequestToMove)
        {
            Debug.Log($"[AudioDebug] PlayResponse skipped for phase {currentPhase} on '{name}'.");
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

        // Debug.Log(
        //     $"[AudioDebug] PlayResponse request agent='{GetAgentName()}' controller='{name}' " +
        //     $"animator='{GetHierarchyPath(animator.transform)}' state='{stateName}' " +
        //     $"phase={currentPhase} attitude={currentAttitude} " +
        //     $"currentShortHash={animator.GetCurrentAnimatorStateInfo(0).shortNameHash} targetHash={stateHash} " +
        //     $"time={Time.time:F3} frame={Time.frameCount}");

        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            // 先立即进入 Idle，再由下方代码重新进入目标动画。
            animator.Play(profile.IdleState, 0, 0f);
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

        // Debug.Log(
        //     $"[AudioDebug] ResetAnimatorForNewTrial agent='{GetAgentName()}' controller='{name}' " +
        //     $"animator='{GetHierarchyPath(animator.transform)}' idle='{profile.IdleState}' " +
        //     $"time={Time.time:F3} frame={Time.frameCount}");

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

        animator.CrossFade(
            Animator.StringToHash(profile.IdleState),
            profile.CrossFadeSeconds,
            0,
            0f
        );

        // Debug.Log(
        //     $"[AudioDebug] PlayIdle agent='{GetAgentName()}' controller='{name}' " +
        //     $"animator='{GetHierarchyPath(animator.transform)}' idle='{profile.IdleState}' " +
        //     $"time={Time.time:F3} frame={Time.frameCount}");
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

    static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
