using System.Linq;
using UnityEngine;

public static class MetaQuestRouteBootstrap
{
    const string VrRigName = "OVRCameraRigInteraction";
    const string CenterEyeName = "CenterEyeAnchor";
    const string LegacyKeyboardPlayerName = "Keyboard Test Player";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ConfigureVrTracking()
    {
        GameObject keyboardPlayer = GameObject.Find(LegacyKeyboardPlayerName);
        if (keyboardPlayer != null)
        {
            Object.Destroy(keyboardPlayer);
        }

        GameObject vrRig = GameObject.Find(VrRigName);
        if (vrRig == null)
        {
            Debug.LogError($"Could not find {VrRigName}; route tracking was not configured.");
            return;
        }

        vrRig.tag = "Player";

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

        recorder.SetTarget(centerEye);

        Rigidbody rigidbody = vrRig.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
        }

        foreach (Trigger trigger in Object.FindObjectsOfType<Trigger>(true))
        {
            trigger.SetTagFilter("Player");
        }

        Debug.Log($"Meta Quest route tracking configured on {VrRigName}; target is {CenterEyeName}.");
    }
}
