using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomPropertyDrawer(typeof(GoapFactValueReference))]
    public sealed class GoapFactValueReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var factProperty = property.FindPropertyRelative("_fact");
            var fact = factProperty.objectReferenceValue as GoapFact;
            var gap = 4f;
            var factRect = new Rect(position.x, position.y, position.width * 0.58f, position.height);
            var valueRect = new Rect(factRect.xMax + gap, position.y, position.width - factRect.width - gap, position.height);
            EditorGUI.PropertyField(factRect, factProperty, GUIContent.none);

            if (fact == null || fact.ValueType == GoapFactType.Boolean)
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("_booleanValue"), GUIContent.none);
            }
            else if (fact.ValueType == GoapFactType.Integer)
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("_integerValue"), GUIContent.none);
            }
            else if (fact.ValueType == GoapFactType.Float)
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("_floatValue"), GUIContent.none);
            }
            else
            {
                var options = new string[fact.EnumOptions.Count];
                for (var index = 0; index < options.Length; index++)
                {
                    options[index] = fact.EnumOptions[index];
                }

                var enumProperty = property.FindPropertyRelative("_enumValue");
                enumProperty.intValue = EditorGUI.Popup(
                    valueRect,
                    fact.NormalizeEnumIndex(enumProperty.intValue),
                    options);
            }

            EditorGUI.EndProperty();
        }
    }
}
