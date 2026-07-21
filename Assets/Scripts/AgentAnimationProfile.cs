using UnityEngine;

[CreateAssetMenu(fileName = "AgentAnimationProfile", menuName = "Politeness Strategy/Agent Animation Profile")]
public class AgentAnimationProfile : ScriptableObject
{
    [SerializeField] RuntimeAnimatorController controller;
    [SerializeField] string idleState = "Idle";
    [SerializeField] string politeState = "Would_You_Like_To_Come_Here";
    [SerializeField] string neutralState = "This_Place_Is_Waiting_For_You";
    [SerializeField] string hostileState = "Come_here";
    [SerializeField] string praiseState = "Welcome_back";
    [SerializeField] string ignoreState = "";
    [SerializeField, Min(0f)] float crossFadeSeconds = 0.05f;

    public RuntimeAnimatorController Controller => controller;
    public string IdleState => idleState;
    public string PraiseState => praiseState;
    public string IgnoreState => ignoreState;
    public float CrossFadeSeconds => crossFadeSeconds;

    public string GetStateName(AgentAttitudeController.Attitude attitude)
    {
        switch (attitude)
        {
            case AgentAttitudeController.Attitude.Polite:
                return politeState;
            case AgentAttitudeController.Attitude.Neutral:
                return neutralState;
            case AgentAttitudeController.Attitude.Hostile:
                return hostileState;
            default:
                return politeState;
        }
    }
}
