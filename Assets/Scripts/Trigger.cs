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

    void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(tagFilter) && !other.CompareTag(tagFilter)) return;
        Debug.Log("player entered: " + gameObject.name);
        animator.SetTrigger(animationTriggerName);
        onTriggerEnter.Invoke();
    }

    void OnTriggerExit(Collider other)
    {
        if (!string.IsNullOrEmpty(tagFilter) && !other.CompareTag(tagFilter)) return;
        Debug.Log("player exited: " + gameObject.name);
        onTriggerExit.Invoke();
    }
}

