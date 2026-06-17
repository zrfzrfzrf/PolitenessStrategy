using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KeyboardTestPlayerSceneSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string PlayerName = "Keyboard Test Player";
    const string CameraName = "Desktop Camera";

    [InitializeOnLoadMethod]
    static void AddToOpenSampleSceneAfterCompile()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                return;
            }

            if (GameObject.Find(PlayerName) != null)
            {
                return;
            }

            CreatePlayerInActiveScene();
        };
    }

    [MenuItem("Tools/Add Keyboard Test Player")]
    public static void AddToSampleScene()
    {
        EditorSceneManager.OpenScene(ScenePath);
        CreatePlayerInActiveScene();
    }

    static void CreatePlayerInActiveScene()
    {
        GameObject existing = GameObject.Find(PlayerName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject player = new GameObject(PlayerName);
        player.transform.position = new Vector3(0f, 0f, -2f);
        TrySetTag(player, "Player");

        CharacterController characterController = player.AddComponent<CharacterController>();
        characterController.center = new Vector3(0f, 0.9f, 0f);
        characterController.height = 1.8f;
        characterController.radius = 0.25f;

        KeyboardPlayerController keyboardController = player.AddComponent<KeyboardPlayerController>();
        PathRecorder pathRecorder = player.AddComponent<PathRecorder>();

        GameObject cameraObject = new GameObject(CameraName);
        cameraObject.transform.SetParent(player.transform);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;
        TrySetTag(cameraObject, "MainCamera");

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 1000f;
        camera.depth = 50f;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        marker.name = "Player Marker";
        marker.transform.SetParent(player.transform);
        marker.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
        Object.DestroyImmediate(marker.GetComponent<Collider>());

        SetSerializedObjectReference(keyboardController, "directionReference", cameraObject.transform);
        SetSerializedObjectReference(pathRecorder, "target", player.transform);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log($"Added {PlayerName} to {ScenePath}. Use WASD to move, Q/E to turn, R to toggle path recording, and P to export CSV.");
    }

    static void TrySetTag(GameObject gameObject, string tag)
    {
        if (InternalEditorUtility.tags.Contains(tag))
        {
            gameObject.tag = tag;
        }
    }

    static void SetSerializedObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
