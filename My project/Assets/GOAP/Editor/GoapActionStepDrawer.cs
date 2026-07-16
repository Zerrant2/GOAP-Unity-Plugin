using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomPropertyDrawer(typeof(GoapActionStep))]
    public sealed class GoapActionStepDrawer : PropertyDrawer
    {
        private const float Gap = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var kind = property.FindPropertyRelative("_kind");
            EditorGUI.PropertyField(line, kind, new GUIContent(label.text));
            line.y += line.height + Gap;

            var selectedKind = (GoapActionStepKind)kind.enumValueIndex;
            switch (selectedKind)
            {
                case GoapActionStepKind.FindSmartObject:
                    Draw(ref line, property, "_targetCategory", "Category");
                    break;
                case GoapActionStepKind.ReserveTarget:
                    Draw(ref line, property, "_timeout", "Queue Timeout");
                    break;
                case GoapActionStepKind.MoveToTarget:
                    Draw(ref line, property, "_targetId", "Named Target (optional)");
                    Draw(ref line, property, "_useNavMesh", "Use NavMesh");
                    Draw(ref line, property, "_moveSpeed", "Move Speed");
                    Draw(ref line, property, "_interactionRange", "Stop Range");
                    break;
                case GoapActionStepKind.Wait:
                    Draw(ref line, property, "_duration", "Duration");
                    break;
                case GoapActionStepKind.InventoryAdd:
                case GoapActionStepKind.InventoryRemove:
                    Draw(ref line, property, "_itemId", "Item Id");
                    Draw(ref line, property, "_amount", "Amount");
                    break;
                case GoapActionStepKind.SetFact:
                case GoapActionStepKind.AddFact:
                case GoapActionStepKind.SubtractFact:
                    Draw(ref line, property, "_factValue", "Fact Value");
                    break;
                case GoapActionStepKind.TriggerAnimation:
                    Draw(ref line, property, "_eventId", "Animator Trigger");
                    break;
                case GoapActionStepKind.InvokeEvent:
                    Draw(ref line, property, "_eventId", "Event Id");
                    break;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var kind = (GoapActionStepKind)property.FindPropertyRelative("_kind").enumValueIndex;
            var extraLines = kind switch
            {
                GoapActionStepKind.FindSmartObject => 1,
                GoapActionStepKind.ReserveTarget => 1,
                GoapActionStepKind.MoveToTarget => 4,
                GoapActionStepKind.Wait => 1,
                GoapActionStepKind.InventoryAdd => 2,
                GoapActionStepKind.InventoryRemove => 2,
                GoapActionStepKind.SetFact => 1,
                GoapActionStepKind.AddFact => 1,
                GoapActionStepKind.SubtractFact => 1,
                GoapActionStepKind.TriggerAnimation => 1,
                GoapActionStepKind.InvokeEvent => 1,
                _ => 0
            };
            return (extraLines + 1) * EditorGUIUtility.singleLineHeight + extraLines * Gap;
        }

        private static void Draw(
            ref Rect line,
            SerializedProperty root,
            string propertyName,
            string label)
        {
            EditorGUI.PropertyField(line, root.FindPropertyRelative(propertyName), new GUIContent(label), true);
            line.y += line.height + Gap;
        }
    }
}
