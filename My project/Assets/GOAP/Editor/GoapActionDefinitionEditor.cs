using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomEditor(typeof(GoapActionDefinition))]
    public sealed class GoapActionDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_description"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_category"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_nodeColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_cost"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_interruptionPolicy"),
                new GUIContent("Goal Interruption"));
            DrawTargeting();

            var execution = serializedObject.FindProperty("_builtInExecution");
            var mode = execution.FindPropertyRelative("_mode");
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(mode);
            var selectedMode = (GoapExecutionMode)mode.enumValueIndex;
            if (selectedMode == GoapExecutionMode.Custom)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_executorId"));
            }
            else
            {
                DrawBuiltInExecution(execution, selectedMode);
            }

            if (selectedMode == GoapExecutionMode.Sequence)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_executionSteps"),
                    new GUIContent("Steps"),
                    true);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_preconditions"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_effects"));
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawBuiltInExecution(SerializedProperty execution, GoapExecutionMode mode)
        {
            if (mode == GoapExecutionMode.Sequence)
            {
                EditorGUILayout.HelpBox(
                    "Steps run from top to bottom. Preconditions and effects remain the planner contract.",
                    MessageType.Info);
                return;
            }

            if (mode == GoapExecutionMode.SmartObjectInteraction)
            {
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_targetCategory"));
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_moveToTarget"));
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_reserveTarget"));
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_consumeTarget"));
                if (execution.FindPropertyRelative("_moveToTarget").boolValue)
                {
                    EditorGUILayout.PropertyField(execution.FindPropertyRelative("_useNavMesh"));
                    EditorGUILayout.PropertyField(execution.FindPropertyRelative("_moveSpeed"));
                    EditorGUILayout.PropertyField(execution.FindPropertyRelative("_interactionRange"));
                }
            }

            EditorGUILayout.PropertyField(execution.FindPropertyRelative("_duration"));
            EditorGUILayout.PropertyField(execution.FindPropertyRelative("_animatorTrigger"));
            var inventoryOperation = execution.FindPropertyRelative("_inventoryOperation");
            EditorGUILayout.PropertyField(inventoryOperation);
            if ((GoapInventoryOperation)inventoryOperation.enumValueIndex != GoapInventoryOperation.None)
            {
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_inventoryItemId"));
                EditorGUILayout.PropertyField(execution.FindPropertyRelative("_inventoryAmount"));
            }
        }

        private void DrawTargeting()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Planning Context", EditorStyles.boldLabel);
            var targetMode = serializedObject.FindProperty("_targetMode");
            EditorGUILayout.PropertyField(targetMode, new GUIContent("Target"));
            var mode = (GoapActionTargetMode)targetMode.enumValueIndex;
            if (mode == GoapActionTargetMode.SmartObjectCategory || mode == GoapActionTargetMode.NamedTarget)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_planningTargetId"),
                    new GUIContent(mode == GoapActionTargetMode.SmartObjectCategory ? "Category" : "Target ID"));
            }

            if (mode == GoapActionTargetMode.SmartObjectCategory)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_includeBusySmartObjects"),
                    new GUIContent("Include Busy"));
            }

            if (mode != GoapActionTargetMode.None)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_distanceCostPerUnit"),
                    new GUIContent("Distance Cost / Unit"));
            }
        }
    }
}
