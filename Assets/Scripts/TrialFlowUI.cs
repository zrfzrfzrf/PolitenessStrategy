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

    static readonly Vector3 StartCenter = Vector3.zero;
    const float StartRadius = 1f;

    Text promptText;
    AgentAttitudeController agentController;
    CamilaAttitudeController camilaController;
    Transform playerTransform;
    float startPromptUntil;
    string startPromptMessage = string.Empty;

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

        MakeButton(canvasObject.transform, "next_trail", new Vector2(-200f, 30f), OnNextTrailForced);
        MakeButton(canvasObject.transform, "restart", new Vector2(200f, 30f), OnRestartClicked);
    }

    void Start()
    {
        // Controllers begin trial 1 in Awake; show the opening subtitle once UI is ready.
        if (!IsWaitingForNextTrial() && !IsSessionComplete())
        {
            ShowTrailStartPrompt();
        }
    }

    void OnRestartClicked()
    {
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
        if (promptText == null)
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

        StartNextTrail();
    }

    void OnNextTrailForced()
    {
        StartNextTrail();
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

        GameObject keyboardPlayer = GameObject.Find(KeyboardPlayerName);
        if (keyboardPlayer != null)
        {
            playerTransform = keyboardPlayer.transform;
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

        if (Camera.main != null)
        {
            playerTransform = Camera.main.transform;
            player = playerTransform;
            return true;
        }

        player = null;
        return false;
    }

    static bool IsInStartZone(Vector3 position)
    {
        float dx = position.x - StartCenter.x;
        float dz = position.z - StartCenter.z;
        return dx * dx + dz * dz <= StartRadius * StartRadius;
    }

    static bool IsFacingNegativeZ(Transform player)
    {
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return true;
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
