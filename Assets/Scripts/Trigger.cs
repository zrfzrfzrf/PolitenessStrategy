using System;
using UnityEngine;
using UnityEngine.Events;

public class Trigger : MonoBehaviour
{
    [SerializeField] string tagFilter;
    [SerializeField] UnityEvent onTriggerEnter;
    [SerializeField] UnityEvent onTriggerExit;
    [SerializeField] Animator animator; 

    public string animationTriggerName; 

    public void SetTagFilter(string requiredTag)
    {
        tagFilter = requiredTag;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!MatchesTagFilter(other)) return;

        Debug.Log("player entered: " + gameObject.name);
        if (animator != null && !string.IsNullOrEmpty(animationTriggerName))
        {
            animator.SetTrigger(animationTriggerName);
        }

        onTriggerEnter.Invoke();
    }

    void OnTriggerExit(Collider other)
    {
        if (!MatchesTagFilter(other)) return;

        Debug.Log("player exited: " + gameObject.name);
        onTriggerExit.Invoke();
    }

    bool MatchesTagFilter(Collider other)
    {
        if (string.IsNullOrEmpty(tagFilter))
        {
            return true;
        }

        return other.CompareTag(tagFilter) || other.transform.root.CompareTag(tagFilter);
    }
}

