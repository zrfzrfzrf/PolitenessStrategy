using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

public class TrialFlowUI : MonoBehaviour
{
    const string ReturnPrompt = "This trail is over. Please return to the starting point.";
    const string RotatePrompt = "Please rotate and face forward.";
    const string KeyboardPlayerName = "player_for_keyboard_test";
    const string VrRigName = "OVRCameraRigInteraction";
    const string CenterEyeName = "CenterEyeAnchor";
    const float StartPromptSeconds = 3f;

    Vector3 startCenter = Vector3.zero;
    const float StartRadius = 1f;

    static readonly Color ZoneCColor = new Color(0.2f, 0.75f, 1f);
    static readonly Color ZoneOColor = new Color(1f, 0.85f, 0.2f);
    static readonly Color ZoneFColor = new Color(0.3f, 0.9f, 0.35f);
    static readonly Color ZoneOffColor = new Color(0.18f, 0.18f, 0.18f, 0.7f);

    Text promptText;
    Image zoneCLight;
    Image zoneOLight;
    Image zoneFLight;
    AgentAttitudeController agentController;
    CamilaAttitudeController camilaController;
    Transform playerTransform;
    float startPromptUntil;
    string startPromptMessage = string.Empty;

    bool isStarted = false;

    void Awake()
    {
        agentController = FindObjectOfType<AgentAttitudeController>();
        camilaController = FindObjectOfType<CamilaAttitudeController>();

        if (agentController == null && camilaController == null)
        {
            Debug.LogWarning("TrialFlowUI could not find AgentAttitudeController or CamilaAttitudeController.");
            return;
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObject = new GameObject("TrialFlowCanvas");
        canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        promptText = MakePrompt(canvasObject.transform);
        MakeZoneLights(canvasObject.transform);

        MakeButton(canvasObject.transform, "Next_trail", new Vector2(-200f, 30f), OnNextTrailForced);
        MakeButton(canvasObject.transform, "Start/Restart", new Vector2(200f, 30f), OnRestartClicked);
    }

    void Start()
    {
        GameObject vrRig = GameObject.Find("OVRCameraRigInteraction");
        if (vrRig != null)
        {
            startCenter = new Vector3(vrRig.transform.position.x, 0f, vrRig.transform.position.z);
        }

        DrawStartZoneCircle();
    }

    void DrawStartZoneCircle()
    {
        LineRenderer lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.startWidth = lr.endWidth = 0.04f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(0f, 0.8f, 1f, 0.8f);

        lr.positionCount = 64;
        for (int i = 0; i < 64; i++)
        {
            float angle = i * 2f * Mathf.PI / 64;
            float x = startCenter.x + Mathf.Cos(angle) * StartRadius;
            float z = startCenter.z + Mathf.Sin(angle) * StartRadius;
            lr.SetPosition(i, new Vector3(x, 0.02f, z));
        }
    }

    void OnRestartClicked()
    {
        isStarted = true;
        if (agentController != null)
        {
            agentController.OnRestartClicked();
        }
        else if (camilaController != null)
        {
            camilaController.OnRestartClicked();
        }

        if (!IsSessionComplete())
        {
            ShowTrailStartPrompt();
        }
    }

    void Update()
    {
        UpdateZoneLights();

        if (promptText == null || !isStarted)
        {
            return;
        }

        if (IsWaitingForNextTrial())
        {
            UpdateWaitingPrompt();
            return;
        }

        if (Time.time < startPromptUntil)
        {
            SetPrompt(startPromptMessage);
            return;
        }

        SetPrompt(string.Empty);
    }

    void UpdateZoneLights()
    {
        SetZoneLight(zoneCLight, ZoneEventBus.InC, ZoneCColor);
        SetZoneLight(zoneOLight, ZoneEventBus.InO, ZoneOColor);
        SetZoneLight(zoneFLight, ZoneEventBus.InF, ZoneFColor);
    }

    static void SetZoneLight(Image light, bool on, Color onColor)
    {
        if (light == null)
        {
            return;
        }

        light.color = on ? onColor : ZoneOffColor;
    }

    void MakeZoneLights(Transform parent)
    {
        GameObject row = new GameObject("ZoneLights");
        row.transform.SetParent(parent, false);

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(1f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(1f, 1f);
        rowRect.anchoredPosition = new Vector2(-24f, -24f);
        rowRect.sizeDelta = new Vector2(220f, 72f);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        zoneCLight = MakeZoneLight(row.transform, "C");
        zoneOLight = MakeZoneLight(row.transform, "O");
        zoneFLight = MakeZoneLight(row.transform, "F");
        UpdateZoneLights();
    }

    static Image MakeZoneLight(Transform parent, string label)
    {
        GameObject go = new GameObject("Light_" + label);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = ZoneOffColor;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(56f, 56f);

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.AddComponent<Text>();
        text.text = label;
        text.fontSize = 28;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        return image;
    }

    void UpdateWaitingPrompt()
    {
        if (!TryGetPlayer(out Transform player))
        {
            SetPrompt(ReturnPrompt);
            return;
        }

        if (!IsInStartZone(player.position))
        {
            SetPrompt(ReturnPrompt);
            return;
        }

        if (IsFacingNegativeZ(player))
        {
            SetPrompt(RotatePrompt);
            return;
        }

        Invoke(nameof(StartNextTrail), 1.5f);
    }

    void OnNextTrailForced()
    {
        isStarted = true;
        CancelInvoke(nameof(StartNextTrail));

        if (IsSessionComplete())
        {
            return;
        }

        if (agentController != null)
        {
            agentController.ForceNextTrial();
        }
        else
        {
            camilaController.ForceNextTrial();
        }

        if (!IsSessionComplete())
        {
            ShowTrailStartPrompt();
        }
        else
        {
            startPromptUntil = 0f;
            SetPrompt(string.Empty);
        }
    }

    void StartNextTrail()
    {
        if (!IsWaitingForNextTrial() || IsSessionComplete())
        {
            return;
        }

        if (agentController != null)
        {
            agentController.OnNextTrialClicked();
        }
        else
        {
            camilaController.OnNextTrialClicked();
        }

        if (!IsSessionComplete())
        {
            ShowTrailStartPrompt();
        }
        else
        {
            startPromptUntil = 0f;
            SetPrompt(string.Empty);
        }
    }

    void ShowTrailStartPrompt()
    {
        startPromptMessage = $"Trail start: {GetProgressLabel()}";
        startPromptUntil = Time.time + StartPromptSeconds;
        SetPrompt(startPromptMessage);
    }

    string GetProgressLabel()
    {
        if (agentController != null)
        {
            return $"{agentController.CurrentTrialNumber}/{agentController.TotalTrials}";
        }

        return AttitudeTrialSession.GetProgressLabel();
    }

    bool IsWaitingForNextTrial()
    {
        if (agentController != null)
        {
            return agentController.IsWaitingForNextTrial;
        }

        return camilaController != null && camilaController.IsWaitingForNextTrial;
    }

    bool IsSessionComplete()
    {
        if (agentController != null)
        {
            return agentController.CompletedTrialCount >= agentController.TotalTrials;
        }

        return AttitudeTrialSession.IsSessionComplete;
    }

    bool TryGetPlayer(out Transform player)
    {
        if (playerTransform != null)
        {
            player = playerTransform;
            return true;
        }

        GameObject vrRig = GameObject.Find(VrRigName);
        if (vrRig != null)
        {
            foreach (Transform child in vrRig.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == CenterEyeName)
                {
                    playerTransform = child;
                    player = playerTransform;
                    return true;
                }
            }
        }

        GameObject keyboardPlayer = GameObject.Find(KeyboardPlayerName);
        if (keyboardPlayer != null)
        {
            playerTransform = keyboardPlayer.transform;
            player = playerTransform;
            return true;
        }

        if (Camera.main != null)
        {
            playerTransform = Camera.main.transform;
            player = playerTransform;
            return true;
        }

        player = null;
        return false;
    }

    bool IsInStartZone(Vector3 position)
    {
        float dx = position.x - startCenter.x;
        float dz = position.z - startCenter.z;
        return dx * dx + dz * dz <= StartRadius * StartRadius;
    }

    static bool IsFacingNegativeZ(Transform player)
    {
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(forward.normalized, Vector3.forward) < 0f;
    }

    void SetPrompt(string message)
    {
        if (promptText.text == message)
        {
            return;
        }

        promptText.text = message;
        promptText.enabled = !string.IsNullOrEmpty(message);
    }

    static Text MakePrompt(Transform parent)
    {
        GameObject go = new GameObject("Prompt");
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.text = string.Empty;
        text.fontSize = 40;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.enabled = false;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.1f, 0.55f);
        rect.anchorMax = new Vector2(0.9f, 0.85f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return text;
    }

    static void MakeButton(Transform parent, string label, Vector2 pos, UnityAction onClick)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        go.AddComponent<Button>().onClick.AddListener(onClick);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(300f, 100f);
        rect.anchoredPosition = pos;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.AddComponent<Text>();
        text.text = label;
        text.fontSize = 36;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
    }
}
