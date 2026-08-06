using UnityEngine;

[CreateAssetMenu(menuName = "Politeness/Strategy Plan")]
public class StrategyPlan : ScriptableObject
{
    [SerializeField] bool shuffleOnStart = true;
    [SerializeField] StrategyTrial[] trials;

    public bool ShuffleOnStart => shuffleOnStart;
    public StrategyTrial[] Trials => trials;
    public int TrialCount => trials != null ? trials.Length : 0;
}
