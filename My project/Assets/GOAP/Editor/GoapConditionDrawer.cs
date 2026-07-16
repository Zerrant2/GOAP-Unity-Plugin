using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomPropertyDrawer(typeof(GoapCondition))]
    public sealed class GoapConditionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var factProperty = property.FindPropertyRelative("_fact");
            var valueProperty = property.FindPropertyRelative("_value");
            var integerProperty = property.FindPropertyRelative("_integerValue");
            var floatProperty = property.FindPropertyRelative("_floatValue");
            var enumProperty = property.FindPropertyRelative("_enumValue");
            var comparisonProperty = property.FindPropertyRelative("_comparison");
            var operationProperty = property.FindPropertyRelative("_effectOperation");
            var isEffect = property.propertyPath.Contains("_effects");
            var fact = factProperty.objectReferenceValue as GoapFact;

            var gap = 4f;
            var factWidth = position.width * 0.46f;
            var operatorWidth = position.width * 0.25f;
            var valueWidth = position.width - factWidth - operatorWidth - gap * 2f;
            var factRect = new Rect(position.x, position.y, factWidth, position.height);
            var operatorRect = new Rect(factRect.xMax + gap, position.y, operatorWidth, position.height);
            var valueRect = new Rect(operatorRect.xMax + gap, position.y, valueWidth, position.height);

            EditorGUI.PropertyField(factRect, factProperty, GUIContent.none);
            if (isEffect)
            {
                using (new EditorGUI.DisabledScope(
                           fact == null || fact.ValueType == GoapFactType.Boolean || fact.ValueType == GoapFactType.Enum))
                {
                    EditorGUI.PropertyField(operatorRect, operationProperty, GUIContent.none);
                }
            }
            else
            {
                if (fact != null &&
                    (fact.ValueType == GoapFactType.Boolean || fact.ValueType == GoapFactType.Enum))
                {
                    var option = comparisonProperty.enumValueIndex == (int)GoapComparison.NotEqual ? 1 : 0;
                    option = EditorGUI.Popup(operatorRect, option, new[] { "Equal", "Not Equal" });
                    comparisonProperty.enumValueIndex = option == 0
                        ? (int)GoapComparison.Equal
                        : (int)GoapComparison.NotEqual;
                }
                else
                {
                    EditorGUI.PropertyField(operatorRect, comparisonProperty, GUIContent.none);
                }
            }

            if (fact == null || fact.ValueType == GoapFactType.Boolean)
            {
                valueProperty.boolValue = EditorGUI.ToggleLeft(
                    valueRect,
                    valueProperty.boolValue ? "True" : "False",
                    valueProperty.boolValue);
            }
            else if (fact.ValueType == GoapFactType.Integer)
            {
                EditorGUI.PropertyField(valueRect, integerProperty, GUIContent.none);
            }
            else if (fact.ValueType == GoapFactType.Float)
            {
                EditorGUI.PropertyField(valueRect, floatProperty, GUIContent.none);
            }
            else
            {
                var options = new string[fact.EnumOptions.Count];
                for (var index = 0; index < options.Length; index++)
                {
                    options[index] = fact.EnumOptions[index];
                }

                enumProperty.intValue = EditorGUI.Popup(
                    valueRect,
                    fact.NormalizeEnumIndex(enumProperty.intValue),
                    options);
            }

            EditorGUI.EndProperty();
        }
    }
}
