using UnityEngine;
using UnityEngine.Animations.Rigging;

public class AgentLookAtBinder : MonoBehaviour
{
    [SerializeField] AgentManager agentManager;
    [SerializeField] Transform lookTarget;
    [SerializeField] string lookTargetName = "Target";
    [SerializeField] float sourceWeight = 1f;
    [SerializeField] bool includeInactive = true;
    [SerializeField] bool rebuildRigBuilder = true;

    void Awake()
    {
        if (agentManager == null)
        {
            agentManager = GetComponent<AgentManager>();
        }
    }

    void OnEnable()
    {
        if (agentManager != null)
        {
            agentManager.OnAgentChanged += BindCurrentAgent;
        }
    }

    void Start()
    {
        BindCurrentAgent();
    }

    void OnDisable()
    {
        if (agentManager != null)
        {
            agentManager.OnAgentChanged -= BindCurrentAgent;
        }
    }

    public void BindCurrentAgent()
    {
        if (agentManager == null)
        {
            Debug.LogWarning("AgentLookAtBinder has no AgentManager assigned.");
            return;
        }

        Animator animator = agentManager.CurrentAnimator;
        if (animator == null)
        {
            return;
        }

        Transform target = ResolveLookTarget();
        if (target == null)
        {
            Debug.LogWarning($"AgentLookAtBinder could not find look target '{lookTargetName}'.");
            return;
        }

        MultiAimConstraint[] constraints = animator.GetComponentsInChildren<MultiAimConstraint>(includeInactive);
        if (constraints.Length == 0)
        {
            Debug.LogWarning($"Agent visual {animator.gameObject.name} has no MultiAimConstraint for look-at binding.");
            return;
        }

        foreach (MultiAimConstraint constraint in constraints)
        {
            MultiAimConstraintData data = constraint.data;
            WeightedTransformArray sources = data.sourceObjects;
            sources.Clear();
            sources.Add(new WeightedTransform(target, sourceWeight));
            data.sourceObjects = sources;
            constraint.data = data;
        }

        if (rebuildRigBuilder)
        {
            RigBuilder rigBuilder = animator.GetComponent<RigBuilder>();
            if (rigBuilder == null)
            {
                rigBuilder = animator.GetComponentInChildren<RigBuilder>(includeInactive);
            }

            if (rigBuilder != null)
            {
                rigBuilder.Build();
            }
        }
    }

    Transform ResolveLookTarget()
    {
        if (lookTarget != null)
        {
            return lookTarget;
        }

        if (string.IsNullOrWhiteSpace(lookTargetName))
        {
            return null;
        }

        GameObject targetObject = GameObject.Find(lookTargetName);
        if (targetObject == null)
        {
            return null;
        }

        lookTarget = targetObject.transform;
        return lookTarget;
    }
}
