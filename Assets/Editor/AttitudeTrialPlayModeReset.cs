#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
static class AttitudeTrialPlayModeReset
{
    static AttitudeTrialPlayModeReset()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
        {
            return;
        }

        AttitudeTrialSession.ResetSession();
    }
}
#endif
