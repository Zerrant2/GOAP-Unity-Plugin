using UnityEditor;

namespace Practice.GOAP.Editor
{
    [CustomEditor(typeof(GoapFact))]
    public sealed class GoapFactEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "_defaultValue",
                "_defaultInteger",
                "_defaultFloat",
                "_enumOptions",
                "_defaultEnumIndex");

            var type = (GoapFactType)serializedObject.FindProperty("_valueType").enumValueIndex;
            if (type == GoapFactType.Enum)
            {
                var optionsProperty = serializedObject.FindProperty("_enumOptions");
                EditorGUILayout.PropertyField(optionsProperty, true);
                if (optionsProperty.arraySize == 0)
                {
                    optionsProperty.arraySize = 1;
                    optionsProperty.GetArrayElementAtIndex(0).stringValue = "None";
                }

                var labels = new string[optionsProperty.arraySize];
                for (var index = 0; index < labels.Length; index++)
                {
                    var value = optionsProperty.GetArrayElementAtIndex(index).stringValue;
                    labels[index] = string.IsNullOrWhiteSpace(value) ? $"Value {index}" : value;
                }

                var defaultEnum = serializedObject.FindProperty("_defaultEnumIndex");
                defaultEnum.intValue = EditorGUILayout.Popup("Default Value", defaultEnum.intValue, labels);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var defaultProperty = type switch
            {
                GoapFactType.Integer => serializedObject.FindProperty("_defaultInteger"),
                GoapFactType.Float => serializedObject.FindProperty("_defaultFloat"),
                _ => serializedObject.FindProperty("_defaultValue")
            };
            EditorGUILayout.PropertyField(defaultProperty, new UnityEngine.GUIContent("Default Value"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
