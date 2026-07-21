using UnityEngine;

public class PlayAudioOnStateEnter : StateMachineBehaviour
{
    public AudioClip audioClip;
    AudioSource audioSource;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (audioSource == null)
        {
            audioSource = animator.gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = animator.gameObject.GetComponentInParent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = animator.gameObject.GetComponentInChildren<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = animator.gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
            }
        }

        if (audioClip != null)
        {
            audioSource.PlayOneShot(audioClip);
        }
    }
}
