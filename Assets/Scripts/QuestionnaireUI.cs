using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static QuestionnaireData;

// World-space questionnaire panel with a dimmed backdrop, answered with the right controller ray + trigger
// (mouse in the editor without a VR rig). One question per page, 9 options, Confirm to continue.
public class QuestionnaireUI : MonoBehaviour
{
    const float PanelDistance = 1.5f;
    const float CanvasScale = 0.0013f;

    static readonly Color NormalColor = new Color(0.25f, 0.25f, 0.25f);
    static readonly Color HoverColor = new Color(0.4f, 0.4f, 0.4f);
    static readonly Color SelectedColor = new Color(0.2f, 0.6f, 1f);
    static readonly Color DisabledColor = new Color(0.12f, 0.12f, 0.12f);

    readonly Button[] buttons = new Button[10];
    readonly Text[] labels = new Text[9];

    Material overlay;
    Canvas dim, panel;
    Text titleText, questionText;
    LineRenderer ray;
    Camera view;
    Transform rightHand;

    string title;
    Question[] questions;
    int[] answers;
    int index;
    int selected;
    Action<int[]> onDone;

    public bool IsOpen => panel.gameObject.activeSelf;

    void Awake()
    {
        overlay = new Material(Resources.Load<Shader>("UIOverlay"));

        dim = MakeCanvas("Dim", new Vector2(4000f, 4000f), -1);
        MakeImage(dim.transform, Vector2.zero, new Vector2(4000f, 4000f), new Color(0f, 0f, 0f, 0.8f));

        panel = MakeCanvas("Panel", new Vector2(1600f, 700f), 0);
        MakeImage(panel.transform, Vector2.zero, new Vector2(1600f, 700f), new Color(0.1f, 0.1f, 0.1f, 0.95f));
        titleText = MakeText(panel.transform, new Vector2(0f, 300f), new Vector2(1500f, 60f), 36);
        questionText = MakeText(panel.transform, new Vector2(0f, 170f), new Vector2(1500f, 130f), 44);

        for (int i = 0; i < 9; i++)
        {
            int value = i + 1;
            float x = (i - 4) * 170f;
            buttons[i] = MakeButton(panel.transform, new Vector2(x, 40f), new Vector2(150f, 90f), value.ToString(), () => selected = value);
            labels[i] = MakeText(panel.transform, new Vector2(x, -70f), new Vector2(165f, 120f), 26);
        }

        buttons[9] = MakeButton(panel.transform, new Vector2(0f, -250f), new Vector2(300f, 90f), "Confirm", Confirm);

        ray = gameObject.AddComponent<LineRenderer>();
        ray.positionCount = 2;
        ray.startWidth = ray.endWidth = 0.005f;
        ray.material = overlay;
        ray.sortingOrder = 1;
        ray.enabled = false;
    }

    public void Run(string title, Question[] questions, Action<int[]> onDone)
    {
        this.title = title;
        this.questions = questions;
        this.onDone = onDone;
        answers = new int[questions.Length];
        index = 0;

        if (questions.Length == 0)
        {
            onDone(answers);
            return;
        }

        GameObject eye = GameObject.Find("CenterEyeAnchor");
        view = eye != null ? eye.GetComponent<Camera>() : Camera.allCameras[0];
        Transform head = view.transform;
        GameObject hand = GameObject.Find("RightControllerAnchor");
        rightHand = hand != null && OVRManager.isHmdPresent ? hand.transform : null;

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        panel.transform.SetPositionAndRotation(head.position + forward * PanelDistance, Quaternion.LookRotation(forward));
        dim.transform.SetParent(head, false);
        dim.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, 0.5f), Quaternion.identity);

        dim.gameObject.SetActive(true);
        panel.gameObject.SetActive(true);
        ray.enabled = rightHand != null;
        ShowQuestion();
    }

    public void Close()
    {
        dim.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        ray.enabled = false;
    }

    void ShowQuestion()
    {
        Question question = questions[index];
        titleText.text = $"{title}  ({index + 1}/{questions.Length})";
        questionText.text = question.Text;
        for (int i = 0; i < 9; i++)
        {
            labels[i].text = question.Labels[i];
        }

        selected = 0;
    }

    void Confirm()
    {
        answers[index] = selected;
        if (++index < questions.Length)
        {
            ShowQuestion();
            return;
        }

        Close();
        onDone(answers);
    }

    void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        Ray pointer;
        bool click;
        if (rightHand != null)
        {
            pointer = new Ray(rightHand.position, rightHand.forward);
            click = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        }
        else
        {
            pointer = view.ScreenPointToRay(Input.mousePosition);
            click = Input.GetMouseButtonDown(0);
        }

        bool onPanel = new Plane(panel.transform.forward, panel.transform.position).Raycast(pointer, out float distance);
        Vector3 hit = pointer.GetPoint(onPanel ? distance : 3f);
        if (ray.enabled)
        {
            ray.SetPosition(0, pointer.origin);
            ray.SetPosition(1, hit);
        }

        buttons[9].interactable = selected > 0;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            bool over = onPanel && button.interactable
                && ((RectTransform)button.transform).rect.Contains(button.transform.InverseTransformPoint(hit));
            button.image.color = !button.interactable ? DisabledColor
                : i == selected - 1 ? SelectedColor
                : over ? HoverColor : NormalColor;

            if (over && click)
            {
                button.onClick.Invoke();
                click = false;
            }
        }
    }

    Canvas MakeCanvas(string canvasName, Vector2 size, int order)
    {
        var go = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * CanvasScale;
        ((RectTransform)go.transform).sizeDelta = size;

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = order;
        go.SetActive(false);
        return canvas;
    }

    Image MakeImage(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = color;
        image.material = overlay;
        return image;
    }

    Text MakeText(Transform parent, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.UpperCenter;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.material = overlay;
        return text;
    }

    Button MakeButton(Transform parent, Vector2 position, Vector2 size, string label, UnityAction onClick)
    {
        Image image = MakeImage(parent, position, size, NormalColor);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        Text text = MakeText(image.transform, Vector2.zero, size, 40);
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }
}
