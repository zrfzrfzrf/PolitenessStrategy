using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Trigger : MonoBehaviour
{
    [SerializeField] string tagFilter;
    [SerializeField, Min(0.1f)] float dwellConfirmSeconds = 1f;
    [SerializeField, Min(0f)] float maxPresenceSpeed = 0.2f;
    [SerializeField] UnityEvent onTriggerEnter;
    [SerializeField] UnityEvent onTriggerExit;
    [SerializeField] Animator animator;

    public string animationTriggerName;

    readonly List<float> speedSamples = new List<float>();
    readonly List<float> sampleTimes = new List<float>();

    Transform trackedTransform;
    Vector3 lastPosition;
    float enterTime;
    bool isInside;
    bool isPresenceConfirmed;

    public void SetTagFilter(string requiredTag)
    {
        tagFilter = requiredTag;
    }

    void Update()
    {
        if (!isInside || isPresenceConfirmed || trackedTransform == null)
        {
            return;
        }

        float now = Time.time;
        Vector3 delta = trackedTransform.position - lastPosition;
        delta.y = 0f;
        speedSamples.Add(delta.magnitude / Time.deltaTime);
        sampleTimes.Add(now);
        lastPosition = trackedTransform.position;

        float cutoff = now - dwellConfirmSeconds;
        while (sampleTimes.Count > 0 && sampleTimes[0] < cutoff)
        {
            sampleTimes.RemoveAt(0);
            speedSamples.RemoveAt(0);
        }

        if (now - enterTime < dwellConfirmSeconds || speedSamples.Count == 0)
        {
            return;
        }

        if (GetMedianSpeed() > maxPresenceSpeed)
        {
            return;
        }

        ConfirmPresence();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!MatchesTagFilter(other))
        {
            return;
        }

        trackedTransform = other.transform.root;
        lastPosition = trackedTransform.position;
        enterTime = Time.time;
        speedSamples.Clear();
        sampleTimes.Clear();
        isInside = true;
        isPresenceConfirmed = false;

        Debug.Log("player entered: " + gameObject.name);
    }

    void OnTriggerExit(Collider other)
    {
        if (!MatchesTagFilter(other) || !isInside)
        {
            return;
        }

        isInside = false;
        isPresenceConfirmed = false;
        speedSamples.Clear();
        sampleTimes.Clear();
        trackedTransform = null;

        Debug.Log("player exited: " + gameObject.name);
        onTriggerExit.Invoke();
    }

    void ConfirmPresence()
    {
        isPresenceConfirmed = true;
        Debug.Log("player presence confirmed: " + gameObject.name);

        if (animator != null && !string.IsNullOrEmpty(animationTriggerName))
        {
            animator.SetTrigger(animationTriggerName);
        }

        onTriggerEnter.Invoke();
    }

    float GetMedianSpeed()
    {
        float[] speeds = speedSamples.ToArray();
        Array.Sort(speeds);
        int mid = speeds.Length / 2;
        return speeds.Length % 2 == 0
            ? (speeds[mid - 1] + speeds[mid]) * 0.5f
            : speeds[mid];
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
