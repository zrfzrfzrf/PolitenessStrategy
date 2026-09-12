using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Trigger : MonoBehaviour
{
    [SerializeField] string tagFilter;
    [SerializeField, Min(0.1f)] float dwellConfirmSeconds = 1f;
    [SerializeField, Min(0f)] float maxPresenceSpeed = 0.2f;
    [SerializeField] bool requireFacing;
    [SerializeField] Vector3 requiredForward = new Vector3(0f, 0f, -1f);
    [SerializeField, Min(0f)] float facingToleranceDegrees = 30f;
    [SerializeField] UnityEvent onTriggerEnter;
    [SerializeField] UnityEvent onTriggerExit;
    [SerializeField] Animator animator;

    public string animationTriggerName;

    [SerializeField] string zoneId;
    const string CenterEyeName = "CenterEyeAnchor";

    readonly List<float> speedSamples = new List<float>();
    readonly List<float> sampleTimes = new List<float>();

    Transform trackedTransform;
    Transform facingTransform;
    Vector3 lastPosition;
    float enterTime;
    bool isInside;
    bool isPresenceConfirmed;

    public void SetTagFilter(string requiredTag)
    {
        tagFilter = requiredTag;
    }

    void Awake()
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            int lastSpace = gameObject.name.LastIndexOf(' ');
            zoneId = lastSpace >= 0 && lastSpace < gameObject.name.Length - 1
                ? gameObject.name.Substring(lastSpace + 1)
                : gameObject.name;
        }
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

        if (requireFacing && !IsFacingRequiredDirection())
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
        facingTransform = FindFacingTransform(trackedTransform);
        lastPosition = trackedTransform.position;
        enterTime = Time.time;
        speedSamples.Clear();
        sampleTimes.Clear();
        isInside = true;
        isPresenceConfirmed = false;

        Debug.Log("player entered: " + gameObject.name);
        ZoneEventBus.Report(zoneId, "enter");
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
        facingTransform = null;

        Debug.Log("player exited: " + gameObject.name);
        ZoneEventBus.Report(zoneId, "exit");
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

        ZoneEventBus.Report(zoneId, "trigger");
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

    bool IsFacingRequiredDirection()
    {
        Transform facing = facingTransform != null ? facingTransform : trackedTransform;
        if (facing == null)
        {
            return false;
        }

        Vector3 forward = facing.forward;
        forward.y = 0f;
        Vector3 required = requiredForward;
        required.y = 0f;

        if (forward.sqrMagnitude < 0.0001f || required.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Angle(forward.normalized, required.normalized) <= facingToleranceDegrees;
    }

    static Transform FindFacingTransform(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == CenterEyeName)
            {
                return child;
            }
        }

        return root;
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
