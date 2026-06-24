using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MetaQuestSceneSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string VrRigName = "OVRCameraRigInteraction";
    const string CenterEyeName = "CenterEyeAnchor";
    const string LegacyKeyboardPlayerName = "Keyboard Test Player";
    const string KeyboardTestPlayerName = "player_for_keyboard_test";

    [InitializeOnLoadMethod]
    static void ConfigureOpenSceneAfterCompile()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().path != ScenePath)
            {
                return;
            }

            ConfigureActiveScene();
        };
    }

    [MenuItem("Tools/Configure Meta Quest Route Tracking")]
    public static void ConfigureSampleScene()
    {
        EditorSceneManager.OpenScene(ScenePath);
        ConfigureActiveScene();
    }

    static void ConfigureActiveScene()
    {
        GameObject legacyKeyboardPlayer = GameObject.Find(LegacyKeyboardPlayerName);
        if (legacyKeyboardPlayer != null)
        {
            Object.DestroyImmediate(legacyKeyboardPlayer);
        }

        GameObject vrRig = GameObject.Find(VrRigName);
        if (vrRig != null)
        {
            ConfigureVrRig(vrRig);
        }
        else
        {
            Debug.LogWarning($"Could not find {VrRigName} in {ScenePath}.");
        }

        GameObject keyboardPlayer = GameObject.Find(KeyboardTestPlayerName);
        if (keyboardPlayer != null)
        {
            ConfigureKeyboardTestPlayer(keyboardPlayer);
        }

        foreach (Trigger trigger in Object.FindObjectsOfType<Trigger>(true))
        {
            SerializedObject serializedTrigger = new SerializedObject(trigger);
            SerializedProperty tagFilter = serializedTrigger.FindProperty("tagFilter");
            tagFilter.stringValue = "Player";
            serializedTrigger.ApplyModifiedProperties();
        }

        if (vrRig != null)
        {
            EditorUtility.SetDirty(vrRig);
        }

        if (keyboardPlayer != null)
        {
            EditorUtility.SetDirty(keyboardPlayer);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log(
            $"Configured route tracking: VR rig={(vrRig != null ? VrRigName : "missing")}, keyboard test player={(keyboardPlayer != null ? KeyboardTestPlayerName : "missing")}, trigger filters use Player.");
    }

    static void ConfigureVrRig(GameObject vrRig)
    {
        if (InternalEditorUtility.tags.Contains("Player"))
        {
            vrRig.tag = "Player";
        }

        Transform centerEye = vrRig.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == CenterEyeName);

        if (centerEye == null)
        {
            Debug.LogError($"Could not find {CenterEyeName} below {VrRigName}.");
            return;
        }

        DataExport recorder = vrRig.GetComponent<DataExport>();
        if (recorder == null)
        {
            recorder = vrRig.AddComponent<DataExport>();
        }

        SetObjectReference(recorder, "target", centerEye);

        Rigidbody rigidbody = vrRig.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
        }
    }

    static void ConfigureKeyboardTestPlayer(GameObject keyboardPlayer)
    {
        if (InternalEditorUtility.tags.Contains("Player"))
        {
            keyboardPlayer.tag = "Player";
        }

        CharacterController characterController = keyboardPlayer.GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = keyboardPlayer.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.center = new Vector3(0f, 1f, 0f);
        }

        CapsuleCollider capsuleCollider = keyboardPlayer.GetComponent<CapsuleCollider>();
        if (capsuleCollider != null)
        {
            capsuleCollider.enabled = false;
        }

        KeyboardPlayerController movement = keyboardPlayer.GetComponent<KeyboardPlayerController>();
        if (movement != null && Camera.main != null)
        {
            movement.SetDirectionReference(Camera.main.transform);
        }

        DataExport recorder = keyboardPlayer.GetComponent<DataExport>();
        if (recorder == null)
        {
            recorder = keyboardPlayer.AddComponent<DataExport>();
        }

        SetObjectReference(recorder, "target", keyboardPlayer.transform);
    }

    static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedProperties();
    }
}
