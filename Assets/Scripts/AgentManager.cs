using System;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    [SerializeField] Transform visualSlot;
    [SerializeField] AgentDefinition currentAgent;
    [SerializeField] bool instantiateOnAwake = true;

    GameObject currentVisual;
    Animator currentAnimator;

    public event Action OnAgentChanged;

    public AgentDefinition CurrentAgent => currentAgent;
    public AgentAnimationProfile CurrentAnimationProfile =>
        currentAgent != null ? currentAgent.AnimationProfile : null;
    public Animator CurrentAnimator => currentAnimator;

    void Awake()
    {
        if (visualSlot == null)
        {
            visualSlot = transform;
        }

        if (instantiateOnAwake)
        {
            ApplyCurrentAgent();
        }
    }

    public void SetCurrentAgent(AgentDefinition agent)
    {
        currentAgent = agent;
        ApplyCurrentAgent();
    }

    public void ApplyCurrentAgent()
    {
        ClearCurrentVisual();
        currentAnimator = null;

        if (currentAgent == null)
        {
            Debug.LogWarning("AgentManager has no AgentDefinition assigned.");
            OnAgentChanged?.Invoke();
            return;
        }

        if (currentAgent.VisualPrefab == null)
        {
            Debug.LogWarning($"AgentDefinition {currentAgent.name} has no visual prefab assigned.");
            OnAgentChanged?.Invoke();
            return;
        }

        currentVisual = Instantiate(currentAgent.VisualPrefab, visualSlot);
        currentVisual.name = currentAgent.AgentId;
        currentVisual.transform.localPosition = currentAgent.LocalPositionOffset;
        currentVisual.transform.localRotation = Quaternion.Euler(currentAgent.LocalEulerOffset);
        currentVisual.transform.localScale = currentAgent.LocalScale;

        currentAnimator = currentVisual.GetComponentInChildren<Animator>();
        if (currentAnimator == null)
        {
            Debug.LogWarning($"Agent visual {currentVisual.name} has no Animator.");
            OnAgentChanged?.Invoke();
            return;
        }

        AgentAnimationProfile profile = currentAgent.AnimationProfile;
        if (profile != null && profile.Controller != null)
        {
            currentAnimator.runtimeAnimatorController = profile.Controller;
        }

        OnAgentChanged?.Invoke();
    }

    void ClearCurrentVisual()
    {
        if (currentVisual == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(currentVisual);
        }
        else
        {
            DestroyImmediate(currentVisual);
        }
    }
}
