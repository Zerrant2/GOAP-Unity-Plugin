using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomPropertyDrawer(typeof(GoapGoalScoreModifier))]
    public sealed class GoapGoalScoreModifierDrawer : PropertyDrawer
    {
        private const float Gap = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 3f + Gap * 2f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = EditorGUIUtility.singleLineHeight;
            var factRect = new Rect(position.x, position.y, position.width, line);
            var inputRect = new Rect(position.x, factRect.yMax + Gap, position.width, line);
            var scoreRect = new Rect(position.x, inputRect.yMax + Gap, position.width, line);

            EditorGUI.PropertyField(factRect, property.FindPropertyRelative("_fact"), label);
            DrawRange(
                inputRect,
                "Fact Range",
                property.FindPropertyRelative("_inputMinimum"),
                property.FindPropertyRelative("_inputMaximum"));

            var clamp = property.FindPropertyRelative("_clampInput");
            var clampWidth = 58f;
            var rangeRect = new Rect(scoreRect.x, scoreRect.y, scoreRect.width - clampWidth - 4f, line);
            DrawRange(
                rangeRect,
                "Score Range",
                property.FindPropertyRelative("_scoreAtMinimum"),
                property.FindPropertyRelative("_scoreAtMaximum"));
            clamp.boolValue = EditorGUI.ToggleLeft(
                new Rect(rangeRect.xMax + 4f, scoreRect.y, clampWidth, line),
                "Clamp",
                clamp.boolValue);
            EditorGUI.EndProperty();
        }

        private static void DrawRange(
            Rect position,
            string label,
            SerializedProperty minimum,
            SerializedProperty maximum)
        {
            var content = EditorGUI.PrefixLabel(position, new GUIContent(label));
            var width = (content.width - 18f) * 0.5f;
            EditorGUI.PropertyField(
                new Rect(content.x, content.y, width, content.height),
                minimum,
                GUIContent.none);
            EditorGUI.LabelField(
                new Rect(content.x + width + 2f, content.y, 14f, content.height),
                "to");
            EditorGUI.PropertyField(
                new Rect(content.x + width + 18f, content.y, width, content.height),
                maximum,
                GUIContent.none);
        }
    }
}
