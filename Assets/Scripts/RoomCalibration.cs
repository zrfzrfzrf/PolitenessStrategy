using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Two-point room calibration: lines the virtual scene up with the real room.
// The operator wears the headset (passthrough on, scene hidden), points the right controller ray at floor mark A
// (virtual point pointA) and floor mark B (virtual point = the rig's spawn position in the scene), and the rig root
// is rotated/translated so the two real points land on the two virtual ones.
// Right controller only: TRIGGER mark (in the preview: start over), B undo, A confirm, A+B together re-open after confirming.
public class RoomCalibration : MonoBehaviour
{
    public static bool InProgress { get; private set; }

    [SerializeField, Tooltip("Virtual world XZ of floor mark A.")] Vector2 pointA = Vector2.zero;
    [SerializeField, Min(0.01f), Tooltip("Largest allowed error (m) between measured and expected A-B distance.")]
    float distanceTolerance = 0.1f;
    [SerializeField, Min(0.5f)] float maxRayDistance = 3f;

    enum State { Marking, Preview, Done }

    const int CalibLayer = 1;
    const string Tag = "[RoomCalibration] ";

    readonly List<Vector3> marks = new List<Vector3>(); // floor points in tracking space: [0] = A, [1] = B
    readonly List<Vector3> lastMarks = new List<Vector3>(); // marks of the last confirmed calibration
    readonly LineRenderer[] markRings = new LineRenderer[2];
    readonly LineRenderer[] targetRings = new LineRenderer[2];
    Vector2[] targets; // virtual XZ of A and B
    State state;
    Transform ts, hand, eye, hudRoot;
    Camera cam;
    OVRPassthroughLayer passthrough;
    GameObject visuals;
    LineRenderer ray, cursor;
    Text hud;
    string flash;
    float flashUntil;
    bool comboHeld;
    bool passthroughReady;
    CameraClearFlags savedFlags;
    Color savedColor;
    int savedMask;

    void Awake()
    {
        OVRCameraRig rig = GetComponentInChildren<OVRCameraRig>();
        ts = rig.trackingSpace;
        hand = rig.rightControllerAnchor;
        eye = rig.centerEyeAnchor;
        cam = eye.GetComponent<Camera>();
        savedFlags = cam.clearFlags;
        savedColor = cam.backgroundColor;
        savedMask = cam.cullingMask;
        targets = new[] { pointA, new Vector2(transform.position.x, transform.position.z) };

        // Stage origin is tied to the Guardian and ignores recenters; a Floor Level origin moves when the headset is put back on.
        OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.Stage;
        OVRManager.instance.isInsightPassthroughEnabled = true;
        GameObject layer = new GameObject("CalibrationPassthrough");
        layer.SetActive(false); // set the overlay type before the layer is created in OnEnable
        layer.transform.SetParent(transform, false);
        passthrough = layer.AddComponent<OVRPassthroughLayer>();
        passthrough.overlayType = OVROverlay.OverlayType.Underlay;
        layer.SetActive(true);

        BuildVisuals();
        Debug.Log($"{Tag}ready. tracking origin={OVRManager.instance.trackingOriginType}, " +
            $"A={targets[0]}, B={targets[1]}, expected A-B {Vector2.Distance(targets[0], targets[1]):F3} m, " +
            $"tolerance {distanceTolerance:F2} m, max ray {maxRayDistance:F1} m, " +
            $"passthrough supported={OVRManager.IsInsightPassthroughSupported()}");
        if (OVRManager.instance.trackingOriginType != OVRManager.TrackingOrigin.Stage)
        {
            Debug.LogWarning($"{Tag}could not switch the tracking origin to Stage (is the headset connected?).");
        }

        Enter(State.Marking, "startup");
    }

    void OnEnable()
    {
        OVRManager.HMDMounted += OnHmdMounted;
        OVRManager.HMDUnmounted += OnHmdUnmounted;
        OVRManager.TrackingAcquired += OnTrackingAcquired;
        OVRManager.TrackingLost += OnTrackingLost;
        OVRManager.VrFocusAcquired += OnVrFocusAcquired;
        OVRManager.VrFocusLost += OnVrFocusLost;
        OVRManager.display.RecenteredPose += OnRecentered;
    }

    void OnDisable()
    {
        OVRManager.HMDMounted -= OnHmdMounted;
        OVRManager.HMDUnmounted -= OnHmdUnmounted;
        OVRManager.TrackingAcquired -= OnTrackingAcquired;
        OVRManager.TrackingLost -= OnTrackingLost;
        OVRManager.VrFocusAcquired -= OnVrFocusAcquired;
        OVRManager.VrFocusLost -= OnVrFocusLost;
        OVRManager.display.RecenteredPose -= OnRecentered;
    }

    void OnDestroy() => InProgress = false;

    void OnHmdMounted() => SystemEvent("HMDMounted", true);
    void OnHmdUnmounted() => SystemEvent("HMDUnmounted");
    void OnTrackingAcquired() => SystemEvent("TrackingAcquired", true);
    void OnTrackingLost() => SystemEvent("TrackingLost");
    void OnVrFocusAcquired() => SystemEvent("VrFocusAcquired");
    void OnVrFocusLost() => SystemEvent("VrFocusLost");
    void OnRecentered() => SystemEvent("RecenteredPose", true);

    // System events that can move the tracking origin re-open the calibration, so the operator must check (or redo) it.
    void SystemEvent(string name, bool originMayChange = false)
    {
        Vector3 head = ts.InverseTransformPoint(eye.position);
        float yaw = Quaternion.LookRotation(ts.InverseTransformDirection(eye.forward)).eulerAngles.y;
        Debug.LogWarning($"{Tag}{name} | state={state} | head in tracking space ({head.x:F2}, {head.z:F2}) yaw {yaw:F0}");
        if (state != State.Done)
        {
            return;
        }

        TrialEventBus.Report("calib_" + name);
        if (originMayChange)
        {
            Enter(State.Preview, name + ": tracking origin may have changed, check the yellow rings");
        }
    }

    // The same two floor marks measured twice: the difference between the two measurements is how far the tracking origin moved.
    void LogOriginShift()
    {
        if (lastMarks.Count < 2)
        {
            return;
        }

        float yaw = Vector3.SignedAngle(lastMarks[1] - lastMarks[0], marks[1] - marks[0], Vector3.up);
        Vector3 offset = marks[0] - Quaternion.AngleAxis(yaw, Vector3.up) * lastMarks[0];
        bool moved = offset.magnitude > 0.05f || Mathf.Abs(yaw) > 1.5f; // above the ~2 cm marking error
        Debug.LogWarning($"{Tag}tracking origin since the last confirmed calibration: yaw {yaw:F2} deg, " +
            $"offset ({offset.x:F3}, {offset.z:F3}) m -> {(moved ? "MOVED" : "stable")}");
    }

    void Update()
    {
        bool ready = OVRManager.IsInsightPassthroughInitialized();
        if (ready != passthroughReady)
        {
            passthroughReady = ready;
            Debug.Log($"{Tag}passthrough initialized={ready} (t={Time.time:F1}s)");
        }

        bool combo = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch)
            && OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        bool reopen = combo && !comboHeld && state == State.Done;
        comboHeld = combo;
        if (reopen)
        {
            Enter(State.Preview, "re-opened with right A+B");
            return;
        }

        if (state == State.Done)
        {
            return;
        }

        if (!combo) // A+B together only re-opens; it must not also undo or confirm
        {
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                Undo();
            }

            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                Confirm();
            }
        }

        // Ray from the right controller, intersected with the floor (tracking-space y = 0).
        Vector3 origin = ts.InverseTransformPoint(hand.position);
        Vector3 dir = ts.InverseTransformDirection(hand.forward);
        float t = dir.y < -0.05f ? -origin.y / dir.y : -1f;
        bool hasHit = t > 0f && t <= maxRayDistance;
        Vector3 hit = origin + dir * t;
        hit.y = 0f;

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            Mark(hasHit, hit, t);
        }

        ray.SetPosition(0, hand.position);
        ray.SetPosition(1, hasHit ? ts.TransformPoint(hit) : hand.position + hand.forward);
        cursor.enabled = hasHit;
        if (hasHit)
        {
            Ring(cursor, ts.TransformPoint(hit), 0.04f);
        }

        Refresh();
    }

    void Mark(bool hasHit, Vector3 hit, float rayDistance)
    {
        if (state == State.Preview) // trigger in the preview starts over
        {
            marks.Clear();
            Enter(State.Marking, "re-mark");
        }

        if (!hasHit)
        {
            Say($"No floor hit. Point the ray down at the floor, within {maxRayDistance:F0} m.");
            return;
        }

        if (marks.Count == 1)
        {
            float measured = Vector3.Distance(marks[0], hit);
            float expected = Vector2.Distance(targets[0], targets[1]);
            if (Mathf.Abs(measured - expected) > distanceTolerance)
            {
                Say($"The two marks are {measured:F2} m apart, expected {expected:F2} m (+-{distanceTolerance:F2}). " +
                    $"Mark the {P(1)} again, or undo the {P(0)} (right B).");
                return;
            }
        }

        string label = P(marks.Count);
        marks.Add(hit);
        Debug.Log($"{Tag}mark {label} at tracking-space ({hit.x:F3}, {hit.z:F3}), ray {rayDistance:F2} m");

        if (marks.Count == 2)
        {
            LogOriginShift();
            Align();
            Enter(State.Preview, "both points marked");
        }
    }

    void Undo()
    {
        if (marks.Count == 0)
        {
            Say("Nothing to undo.");
            return;
        }

        marks.RemoveAt(marks.Count - 1);
        Enter(State.Marking, $"undo, {marks.Count} mark(s) left");
    }

    void Confirm()
    {
        if (state != State.Preview)
        {
            Say("Mark both floor points first.");
            return;
        }

        if (marks.Count == 2) // confirming a kept alignment without re-marking has no new marks
        {
            lastMarks.Clear();
            lastMarks.AddRange(marks);
        }

        Enter(State.Done, "confirmed; right A+B together re-open it");
        TrialEventBus.Report("calib_confirmed");
    }

    // Rotate/translate the rig root (yaw only, height untouched) so real A -> virtual A and real B -> the virtual B direction.
    void Align()
    {
        Vector3 wa = ts.TransformPoint(marks[0]);
        Vector3 wb = ts.TransformPoint(marks[1]);
        Vector3 va = new Vector3(targets[0].x, wa.y, targets[0].y);
        Vector3 vb = new Vector3(targets[1].x, wa.y, targets[1].y);

        float yaw = Vector3.SignedAngle(wb - wa, vb - va, Vector3.up);
        Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up);
        transform.SetPositionAndRotation(va + q * (transform.position - wa), q * transform.rotation);

        float errorB = Vector3.Distance(ts.TransformPoint(marks[1]), vb);
        Vector3 p = transform.position;
        Debug.Log($"{Tag}aligned: yaw {yaw:F2} deg, rig now ({p.x:F3}, {p.y:F3}, {p.z:F3}) yaw {transform.eulerAngles.y:F1}, " +
            $"B error {errorB * 100f:F1} cm");
    }

    // Marking / Preview: passthrough on, scene hidden, only calibration visuals drawn. Done: normal scene.
    void Enter(State next, string reason)
    {
        if (state == State.Done && next == State.Preview)
        {
            marks.Clear(); // old marks live in a tracking space that may no longer exist
        }

        state = next;
        bool calibrating = next != State.Done;
        InProgress = calibrating;
        visuals.SetActive(calibrating);
        passthrough.hidden = !calibrating;
        cam.cullingMask = calibrating ? 1 << CalibLayer : savedMask;
        cam.clearFlags = calibrating ? CameraClearFlags.SolidColor : savedFlags;
        cam.backgroundColor = calibrating ? Color.clear : savedColor;
        Debug.Log($"{Tag}{next} ({reason}); marks={marks.Count}, recording paused={calibrating}");
    }

    // Operator-facing name of floor point i with its virtual coordinates, e.g. "start point (2, 0.8)".
    string P(int i) => $"{(i == 0 ? "origin" : "start point")} ({targets[i].x:0.##}, {targets[i].y:0.##})";

    void Say(string message)
    {
        flash = message;
        flashUntil = Time.time + 4f;
        Debug.LogWarning(Tag + message);
    }

    void Refresh()
    {
        for (int i = 0; i < 2; i++)
        {
            markRings[i].enabled = i < marks.Count;
            if (i < marks.Count)
            {
                Ring(markRings[i], ts.TransformPoint(marks[i]), 0.06f);
            }

            targetRings[i].enabled = state == State.Preview;
            Ring(targetRings[i], new Vector3(targets[i].x, ts.position.y, targets[i].y), 0.12f);
        }

        string hint = state == State.Preview
            ? (marks.Count == 2 ? $"Marks are {Vector3.Distance(marks[0], marks[1]):F2} m apart. " : "Tracking may have changed. ")
                + $"Yellow rings must sit on the marks: {P(0)} and {P(1)}.\nRight A: confirm   "
                + (marks.Count > 0 ? "Right B: undo   " : "") + "TRIGGER: re-mark"
            : $"Aim at the floor mark for the {P(marks.Count)} and press the right TRIGGER ({marks.Count + 1}/2)"
                + (marks.Count > 0 ? "\nRight B: undo" : "");
        if (Time.time > 5f && !passthroughReady)
        {
            hint += "\nPassthrough is not active (see Console)";
        }

        hud.text = Time.time < flashUntil ? flash + "\n\n" + hint : hint;
        hudRoot.SetPositionAndRotation(eye.TransformPoint(0f, -0.1f, 1.2f), eye.rotation);
    }

    static void Ring(LineRenderer line, Vector3 center, float radius)
    {
        center.y += 0.01f;
        for (int i = 0; i < line.positionCount; i++)
        {
            float a = i * 2f * Mathf.PI / line.positionCount;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }

    void BuildVisuals()
    {
        visuals = new GameObject("CalibrationVisuals") { layer = CalibLayer };
        visuals.transform.SetParent(transform, false);

        ray = Line("Ray", Color.white, 2, false);
        cursor = Line("Cursor", Color.white, 24, true);
        for (int i = 0; i < 2; i++)
        {
            markRings[i] = Line("Mark" + i, i == 0 ? Color.cyan : Color.green, 24, true);
            targetRings[i] = Line("Target" + i, Color.yellow, 24, true);
        }

        GameObject canvas = new GameObject("Hud") { layer = CalibLayer };
        canvas.transform.SetParent(visuals.transform, false);
        canvas.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 260f);
        canvas.transform.localScale = Vector3.one * 0.0015f;
        hudRoot = canvas.transform;

        GameObject text = new GameObject("Text") { layer = CalibLayer };
        text.transform.SetParent(canvas.transform, false);
        hud = text.AddComponent<Text>();
        hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hud.fontSize = 40;
        hud.alignment = TextAnchor.MiddleCenter;
        hud.horizontalOverflow = HorizontalWrapMode.Wrap;
        hud.verticalOverflow = VerticalWrapMode.Overflow;
        hud.rectTransform.anchorMin = Vector2.zero;
        hud.rectTransform.anchorMax = Vector2.one;
        hud.rectTransform.offsetMin = hud.rectTransform.offsetMax = Vector2.zero;
        Outline outline = text.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    LineRenderer Line(string name, Color color, int points, bool loop)
    {
        GameObject go = new GameObject(name) { layer = CalibLayer };
        go.transform.SetParent(visuals.transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points;
        line.startWidth = line.endWidth = 0.01f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = color;
        return line;
    }
}
