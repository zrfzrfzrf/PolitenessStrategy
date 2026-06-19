using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class CamilaAttitudeController : MonoBehaviour
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
    [SerializeField, Min(0f)] float sceneReloadDelaySeconds = 2f;
    [SerializeField] Animator animator;
    [SerializeField] bool enableResponses = true;

    const string PoliteAnimationState = "Would_You_Like_To_Come_Here";
    const string NeutralAnimationState = "This_Place_Is_Waiting_For_You";
    const string HostileAnimationState = "Come_here";
    const string IdleAnimationState = "Idle";
    const float AnimationCrossFadeSeconds = 0.05f;

    [SerializeField] AttitudeStateEvent onStateChanged;

    Attitude currentAttitude;
    Attitude currentTrialEnterCAttitude;
    Phase currentPhase;
    bool isPlayerInC;
    bool isTerminal;
    bool isTrialCompleting;
    Coroutine stayAtCCoroutine;
    AttitudeTrialSession.TrialDefinition currentTrial;

    public Attitude CurrentAttitude => currentAttitude;
    public Phase CurrentPhase => currentPhase;
    public bool IsTerminal => isTerminal;
    public bool IsPlayerInC => isPlayerInC;
    public AttitudeTrialSession.TrialDefinition CurrentTrial => currentTrial;

    void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        BeginCurrentTrial();
    }

    [ContextMenu("Reset Attitude Trial Session")]
    public void ResetTrialSession()
    {
        AttitudeTrialSession.ResetSession();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void BeginCurrentTrial()
    {
        CancelStayAtCTimer();

        if (AttitudeTrialSession.IsSessionComplete)
        {
            isTerminal = true;
            currentPhase = Phase.Praise;
            Debug.Log("All 9 attitude trials are complete.");
            return;
        }

        currentTrial = AttitudeTrialSession.GetCurrentTrial();
        currentTrialEnterCAttitude = currentTrial.EnterCAttitude;
        currentAttitude = currentTrial.InitialAttitude;
        currentPhase = Phase.Invitation;
        isPlayerInC = false;
        isTerminal = false;
        isTrialCompleting = false;

        Debug.Log(
            $"Starting trial {AttitudeTrialSession.GetProgressLabel()}: " +
            $"Invitation={currentTrial.InitialAttitude}, EnterC={currentTrial.EnterCAttitude}");

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
        CompleteTrialAndAdvance("Praise");
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
        CompleteTrialAndAdvance("Ignore");
    }

    void CompleteTrialAndAdvance(string outcome)
    {
        if (isTrialCompleting)
        {
            return;
        }

        isTrialCompleting = true;
        AttitudeTrialSession.CompleteCurrentTrial();

        Debug.Log(
            $"Trial completed with {outcome}. Finished {AttitudeTrialSession.CompletedTrialCount}/{AttitudeTrialSession.TotalTrials}.");

        if (AttitudeTrialSession.IsSessionComplete)
        {
            Debug.Log("All 9 attitude trials are complete.");
            return;
        }

        StartCoroutine(ReloadSceneAfterDelay());
    }

    IEnumerator ReloadSceneAfterDelay()
    {
        if (sceneReloadDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(sceneReloadDelaySeconds);
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
        string message = string.IsNullOrEmpty(reason)
            ? $"Camila state: {currentPhase} / {currentAttitude}"
            : $"Camila state: {currentPhase} / {currentAttitude} ({reason})";
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

        if (animator == null)
        {
            Debug.LogWarning("CamilaAttitudeController has no Animator assigned.");
            return;
        }

        string stateName = GetAnimationStateName(currentAttitude);
        int stateHash = Animator.StringToHash(stateName);

        // Replay the same clip when Invitation and EnterC share an attitude.
        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            animator.CrossFade(IdleAnimationState, AnimationCrossFadeSeconds, 0, 0f);
            animator.Update(0f);
        }

        animator.CrossFade(stateHash, AnimationCrossFadeSeconds, 0, 0f);
        Debug.Log($"Camila playing animation: {stateName} ({currentPhase} / {currentAttitude})");
    }

    void ResetAnimatorForNewTrial()
    {
        if (animator == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
        animator.Play(IdleAnimationState, 0, 0f);
        animator.Update(0f);
    }

    static string GetAnimationStateName(Attitude attitude)
    {
        switch (attitude)
        {
            case Attitude.Polite:
                return PoliteAnimationState;
            case Attitude.Neutral:
                return NeutralAnimationState;
            case Attitude.Hostile:
                return HostileAnimationState;
            default:
                return PoliteAnimationState;
        }
    }
}
