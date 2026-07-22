using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomEditor(typeof(GoapGoalDefinition))]
    public sealed class GoapGoalDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_description"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_category"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_nodeColor"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_priority"),
                new GUIContent("Base Score"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_cooldownSeconds"),
                new GUIContent("Cooldown (Seconds)"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_scoreModifiers"),
                new GUIContent("Fact Score Modifiers"),
                true);
            EditorGUILayout.HelpBox(
                "Final score = Base Score + Fact Score Modifiers + scorer components on the Agent.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("State", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_activationConditions"),
                new GUIContent("Active When"),
                true);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_desiredState"),
                new GUIContent("Desired State"),
                true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
