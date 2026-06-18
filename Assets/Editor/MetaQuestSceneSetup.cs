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
        GameObject keyboardPlayer = GameObject.Find(LegacyKeyboardPlayerName);
        if (keyboardPlayer != null)
        {
            Object.DestroyImmediate(keyboardPlayer);
        }

        GameObject vrRig = GameObject.Find(VrRigName);
        if (vrRig == null)
        {
            Debug.LogError($"Could not find {VrRigName} in {ScenePath}.");
            return;
        }

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

        PathRecorder recorder = vrRig.GetComponent<PathRecorder>();
        if (recorder == null)
        {
            recorder = vrRig.AddComponent<PathRecorder>();
        }

        SetObjectReference(recorder, "target", centerEye);

        Rigidbody rigidbody = vrRig.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
        }

        foreach (Trigger trigger in Object.FindObjectsOfType<Trigger>(true))
        {
            SerializedObject serializedTrigger = new SerializedObject(trigger);
            SerializedProperty tagFilter = serializedTrigger.FindProperty("tagFilter");
            tagFilter.stringValue = "Player";
            serializedTrigger.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(vrRig);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log($"Configured {VrRigName}: route target is {CenterEyeName}; trigger filters use Player; keyboard player removed.");
    }

    static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedProperties();
    }
}
