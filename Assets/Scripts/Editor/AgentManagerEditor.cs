using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AgentManager))]
public class AgentManagerEditor : Editor
{
    SerializedProperty visualSlot;
    SerializedProperty currentAgent;
    SerializedProperty instantiateOnAwake;
    SerializedProperty availableAgents;
    SerializedProperty selectedAgentIndex;

    void OnEnable()
    {
        visualSlot = serializedObject.FindProperty("visualSlot");
        currentAgent = serializedObject.FindProperty("currentAgent");
        instantiateOnAwake = serializedObject.FindProperty("instantiateOnAwake");
        availableAgents = serializedObject.FindProperty("availableAgents");
        selectedAgentIndex = serializedObject.FindProperty("selectedAgentIndex");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(visualSlot);
        EditorGUILayout.PropertyField(instantiateOnAwake);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(availableAgents, true);
        DrawAgentDropdown();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(currentAgent);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawAgentDropdown()
    {
        if (availableAgents.arraySize == 0)
        {
            EditorGUILayout.HelpBox("Add AgentDefinition assets to Available Agents to enable the dropdown.", MessageType.Info);
            return;
        }

        string[] labels = new string[availableAgents.arraySize];
        for (int i = 0; i < availableAgents.arraySize; i++)
        {
            SerializedProperty agentProperty = availableAgents.GetArrayElementAtIndex(i);
            AgentDefinition definition = agentProperty.objectReferenceValue as AgentDefinition;
            labels[i] = definition != null ? definition.name : $"Missing Agent {i}";
        }

        int currentIndex = Mathf.Clamp(selectedAgentIndex.intValue, 0, availableAgents.arraySize - 1);
        int newIndex = EditorGUILayout.Popup("Selected Agent", currentIndex, labels);

        if (newIndex == currentIndex)
        {
            return;
        }

        selectedAgentIndex.intValue = newIndex;
        currentAgent.objectReferenceValue = availableAgents.GetArrayElementAtIndex(newIndex).objectReferenceValue;
        serializedObject.ApplyModifiedProperties();

        AgentManager manager = (AgentManager)target;
        if (Application.isPlaying)
        {
            manager.SelectAgentByIndex(newIndex);
        }
        else
        {
            EditorUtility.SetDirty(manager);
        }
    }
}
