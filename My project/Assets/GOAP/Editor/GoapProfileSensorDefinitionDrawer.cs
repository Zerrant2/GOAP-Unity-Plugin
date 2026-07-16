using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomPropertyDrawer(typeof(GoapProfileSensorDefinition))]
    public sealed class GoapProfileSensorDefinitionDrawer : PropertyDrawer
    {
        private const float Gap = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Draw(ref line, property, "_name", label.text);
            var kind = property.FindPropertyRelative("_kind");
            Draw(ref line, kind, "Source");
            Draw(ref line, property, "_fact", "Writes Fact");

            var selectedKind = (GoapProfileSensorKind)kind.enumValueIndex;
            switch (selectedKind)
            {
                case GoapProfileSensorKind.SmartObject:
                    Draw(ref line, property, "_sourceId", "Category");
                    Draw(ref line, property, "_radius", "Max Distance (0 = Any)");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.Inventory:
                    Draw(ref line, property, "_sourceId", "Item Id");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.Distance:
                    Draw(ref line, property, "_targetId", "Named Target");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.Proximity:
                    Draw(ref line, property, "_radius", "Radius");
                    Draw(ref line, property, "_layerMask", "Layer Mask");
                    Draw(ref line, property, "_requiredTag", "Tag (optional)");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.Stat:
                    Draw(ref line, property, "_sourceId", "Stat Id");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.Time:
                    Draw(ref line, property, "_scale", "Scale");
                    Draw(ref line, property, "_offset", "Offset");
                    DrawThreshold(ref line, property);
                    break;
                case GoapProfileSensorKind.ComponentProperty:
                    Draw(ref line, property, "_targetId", "Named Target (optional)");
                    Draw(ref line, property, "_componentType", "Component Type");
                    Draw(ref line, property, "_memberName", "Property / Field");
                    break;
                case GoapProfileSensorKind.Constant:
                    Draw(ref line, property, "_constantValue", "Value");
                    break;
            }

            var updateMode = property.FindPropertyRelative("_updateMode");
            Draw(ref line, updateMode, "Update Mode");
            if ((GoapSensorUpdateMode)updateMode.enumValueIndex == GoapSensorUpdateMode.Interval)
            {
                Draw(ref line, property, "_interval", "Interval");
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var kind = (GoapProfileSensorKind)property.FindPropertyRelative("_kind").enumValueIndex;
            var sourceLines = kind switch
            {
                GoapProfileSensorKind.SmartObject => 4,
                GoapProfileSensorKind.Inventory => 3,
                GoapProfileSensorKind.Distance => 3,
                GoapProfileSensorKind.Proximity => 5,
                GoapProfileSensorKind.Stat => 3,
                GoapProfileSensorKind.Time => 4,
                GoapProfileSensorKind.ComponentProperty => 3,
                GoapProfileSensorKind.Constant => 1,
                _ => 0
            };
            var updateLines = (GoapSensorUpdateMode)property.FindPropertyRelative("_updateMode").enumValueIndex ==
                              GoapSensorUpdateMode.Interval
                ? 2
                : 1;
            var totalLines = 3 + sourceLines + updateLines;
            return totalLines * EditorGUIUtility.singleLineHeight + (totalLines - 1) * Gap;
        }

        private static void DrawThreshold(ref Rect line, SerializedProperty root)
        {
            Draw(ref line, root, "_comparison", "Boolean Comparison");
            Draw(ref line, root, "_threshold", "Boolean Threshold");
        }

        private static void Draw(
            ref Rect line,
            SerializedProperty root,
            string propertyName,
            string label)
        {
            Draw(ref line, root.FindPropertyRelative(propertyName), label);
        }

        private static void Draw(ref Rect line, SerializedProperty property, string label)
        {
            EditorGUI.PropertyField(line, property, new GUIContent(label), true);
            line.y += line.height + Gap;
        }
    }
}
