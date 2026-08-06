using System;

[Serializable]
public class StrategyTrial
{
    public AgentAttitudeController.Attitude invitationAttitude;
    public AgentAttitudeController.Attitude enterCAttitude;
    public AgentAttitudeController.FResponseStrategy directFStrategy;
    public AgentAttitudeController.FResponseStrategy indirectFStrategy;
}
