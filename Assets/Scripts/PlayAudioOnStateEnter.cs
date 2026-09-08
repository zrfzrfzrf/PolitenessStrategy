using UnityEngine;

public class PlayAudioOnStateEnter : StateMachineBehaviour
{
    public AudioClip audioClip;
    AudioSource audioSource;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator == null)
        {
            Debug.LogWarning("[AudioDebug] PlayAudioOnStateEnter fired with no Animator.");
            return;
        }

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
                Debug.Log($"[AudioDebug] Added runtime AudioSource to {GetHierarchyPath(audioSource.transform)} for animator {GetHierarchyPath(animator.transform)}.");
            }
        }

        if (audioClip != null)
        {
            Debug.Log(
                $"[AudioDebug] PlayOneShot clip='{audioClip.name}' " +
                $"animator='{GetHierarchyPath(animator.transform)}' " +
                $"source='{GetHierarchyPath(audioSource.transform)}' " +
                $"layer={layerIndex} shortHash={stateInfo.shortNameHash} fullHash={stateInfo.fullPathHash} " +
                $"time={Time.time:F3} frame={Time.frameCount}");
            audioSource.PlayOneShot(audioClip);
        }
        else
        {
            Debug.Log(
                $"[AudioDebug] State entered with no audio clip " +
                $"animator='{GetHierarchyPath(animator.transform)}' " +
                $"layer={layerIndex} shortHash={stateInfo.shortNameHash} fullHash={stateInfo.fullPathHash} " +
                $"time={Time.time:F3} frame={Time.frameCount}");
        }
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
