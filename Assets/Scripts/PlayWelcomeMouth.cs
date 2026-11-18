using UnityEngine;

public class PlayWelcomeMouth : StateMachineBehaviour
{
    public override void OnStateEnter(
        Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.Play("Face.WelcomeMouth", animator.GetLayerIndex("Face"), 0f);
    }
}
