using UnityEngine;

public static class KeyboardTestPlayerBootstrap
{
    const string PlayerName = "Keyboard Test Player";

#if UNITY_EDITOR || UNITY_STANDALONE
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SpawnKeyboardPlayer()
    {
        if (Object.FindObjectOfType<KeyboardPlayerController>() != null)
        {
            return;
        }

        GameObject player = new GameObject(PlayerName);
        player.transform.position = new Vector3(0f, 0f, -2f);
        TrySetTag(player, "Player");

        CharacterController characterController = player.AddComponent<CharacterController>();
        characterController.center = new Vector3(0f, 0.9f, 0f);
        characterController.height = 1.8f;
        characterController.radius = 0.25f;

        GameObject cameraObject = new GameObject("Desktop Camera");
        cameraObject.transform.SetParent(player.transform);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;
        TrySetTag(cameraObject, "MainCamera");

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.03f;
        camera.depth = 50f;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        marker.name = "Player Marker";
        marker.transform.SetParent(player.transform);
        marker.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
        Object.Destroy(marker.GetComponent<Collider>());

        KeyboardPlayerController keyboardController = player.AddComponent<KeyboardPlayerController>();
        keyboardController.SetDirectionReference(cameraObject.transform);

        PathRecorder pathRecorder = player.AddComponent<PathRecorder>();
        pathRecorder.SetTarget(player.transform);
    }

    static void TrySetTag(GameObject gameObject, string tag)
    {
        try
        {
            gameObject.tag = tag;
        }
        catch (UnityException)
        {
            // The project may not define the optional Player/MainCamera tags.
        }
    }
#endif
}
