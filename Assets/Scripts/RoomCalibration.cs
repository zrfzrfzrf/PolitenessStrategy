using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Two-point room calibration: lines the virtual scene up with the real room.
// The operator wears the headset (passthrough on, scene hidden), points the right controller ray at floor mark A
// (virtual point pointA) and floor mark B (virtual point = the rig's spawn position in the scene), and the rig root
// is rotated/translated so the two real points land on the two virtual ones.
// Right hand: TRIGGER mark, B undo, A confirm.  Left hand: X restart marking, X+Y re-open after confirming.
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
            $"tolerance {distanceTolerance:F2} m, max ray {maxRayDistance:F1} m");
        if (OVRManager.instance.trackingOriginType != OVRManager.TrackingOrigin.FloorLevel)
        {
            Debug.LogWarning($"{Tag}tracking origin is not FloorLevel; the floor is assumed at tracking-space y=0.");
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

    void OnHmdMounted() => SystemEvent("HMDMounted");
    void OnHmdUnmounted() => SystemEvent("HMDUnmounted");
    void OnTrackingAcquired() => SystemEvent("TrackingAcquired");
    void OnTrackingLost() => SystemEvent("TrackingLost");
    void OnVrFocusAcquired() => SystemEvent("VrFocusAcquired");
    void OnVrFocusLost() => SystemEvent("VrFocusLost");
    void OnRecentered() => SystemEvent("RecenteredPose");

    // Origin-related system events. After one of these the calibration may be stale: re-open it with X+Y and check the rings.
    void SystemEvent(string name)
    {
        Vector3 head = ts.InverseTransformPoint(eye.position);
        float yaw = Quaternion.LookRotation(ts.InverseTransformDirection(eye.forward)).eulerAngles.y;
        Debug.LogWarning($"{Tag}{name} | state={state} | head in tracking space ({head.x:F2}, {head.z:F2}) yaw {yaw:F0}");
        if (state == State.Done)
        {
            TrialEventBus.Report("calib_" + name);
        }
    }

    void Update()
    {
        bool combo = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch)
            && OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch);
        bool reopen = combo && !comboHeld && state == State.Done;
        comboHeld = combo;
        if (reopen)
        {
            Enter(State.Preview, "re-opened with X+Y");
            return;
        }

        if (state == State.Done)
        {
            return;
        }

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
        {
            marks.Clear();
            Enter(State.Marking, "restart");
        }

        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            Undo();
        }

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            Confirm();
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
        if (state != State.Marking)
        {
            Say("Both points are marked. A = confirm, B = undo, X = restart.");
            return;
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
                Say($"A-B is {measured:F2} m but should be {expected:F2} m (+-{distanceTolerance:F2}). " +
                    "Mark B again, or undo A.");
                return;
            }
        }

        string label = marks.Count == 0 ? "A" : "B";
        marks.Add(hit);
        Debug.Log($"{Tag}mark {label} at tracking-space ({hit.x:F3}, {hit.z:F3}), ray {rayDistance:F2} m");

        if (marks.Count == 2)
        {
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
            Say("Mark both A and B first.");
            return;
        }

        Enter(State.Done, "confirmed");
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
            ? $"A-B {Vector3.Distance(marks[0], marks[1]):F2} m. Yellow rings must sit on the tape.\nA: confirm   B: undo   X: restart"
            : $"Aim at floor mark {(marks.Count == 0 ? "A" : "B")} and press TRIGGER ({marks.Count + 1}/2)\nB: undo   X: restart";
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
