using System.Linq;
using UnityEngine;

public static class MetaQuestRouteBootstrap
{
    const string VrRigName = "OVRCameraRigInteraction";
    const string CenterEyeName = "CenterEyeAnchor";
    const string LegacyKeyboardPlayerName = "Keyboard Test Player";
    const string KeyboardTestPlayerName = "player_for_keyboard_test";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ConfigureRouteTracking()
    {
        GameObject legacyKeyboardPlayer = GameObject.Find(LegacyKeyboardPlayerName);
        if (legacyKeyboardPlayer != null)
        {
            Object.Destroy(legacyKeyboardPlayer);
        }

        foreach (Trigger trigger in Object.FindObjectsOfType<Trigger>(true))
        {
            trigger.SetTagFilter("Player");
        }

        GameObject vrRig = GameObject.Find(VrRigName);
        GameObject keyboardPlayer = GameObject.Find(KeyboardTestPlayerName);

        if (vrRig != null)
        {
            ConfigureVrRig(vrRig);
        }

        if (keyboardPlayer != null)
        {
            ConfigureKeyboardTestPlayer(keyboardPlayer);
        }

        if (vrRig == null && keyboardPlayer == null)
        {
            Debug.LogError(
                $"Could not find {VrRigName} or {KeyboardTestPlayerName}; route tracking was not configured.");
        }
    }

    static void ConfigureVrRig(GameObject vrRig)
    {
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

        Debug.Log($"Meta Quest route tracking configured on {VrRigName}; target is {CenterEyeName}.");
    }

    static void ConfigureKeyboardTestPlayer(GameObject keyboardPlayer)
    {
        keyboardPlayer.tag = "Player";

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

        PathRecorder recorder = keyboardPlayer.GetComponent<PathRecorder>();
        if (recorder == null)
        {
            recorder = keyboardPlayer.AddComponent<PathRecorder>();
        }

        recorder.SetTarget(keyboardPlayer.transform);

        Debug.Log(
            $"Keyboard test player configured on {KeyboardTestPlayerName}; CharacterController enabled for trigger detection.");
    }
}
