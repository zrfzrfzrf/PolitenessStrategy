using UnityEngine;

[CreateAssetMenu(fileName = "AgentDefinition", menuName = "Politeness Strategy/Agent Definition")]
public class AgentDefinition : ScriptableObject
{
    [SerializeField] string agentId;
    [SerializeField] GameObject visualPrefab;
    [SerializeField] AgentAnimationProfile animationProfile;
    [SerializeField] Vector3 localPositionOffset;
    [SerializeField] Vector3 localEulerOffset;
    [SerializeField] Vector3 localScale = Vector3.one;

    public string AgentId => string.IsNullOrEmpty(agentId) ? name : agentId;
    public GameObject VisualPrefab => visualPrefab;
    public AgentAnimationProfile AnimationProfile => animationProfile;
    public Vector3 LocalPositionOffset => localPositionOffset;
    public Vector3 LocalEulerOffset => localEulerOffset;
    public Vector3 LocalScale => localScale == Vector3.zero ? Vector3.one : localScale;
}
