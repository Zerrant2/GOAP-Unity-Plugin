using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    internal static class GoapContentBuilderGui
    {
        private static readonly string[] EqualityOptions = { "Equal", "Not Equal" };

        public static void DrawConditionList(
            string title,
            List<GoapCondition> conditions,
            GoapDomain domain,
            bool isEffect,
            string emptyMessage)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (conditions.Count == 0)
            {
                EditorGUILayout.HelpBox(emptyMessage, MessageType.Info);
            }

            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                EditorGUILayout.BeginHorizontal();
                var nextFact = (GoapFact)EditorGUILayout.ObjectField(
                    condition.Fact,
                    typeof(GoapFact),
                    false,
                    GUILayout.MinWidth(145f));
                if (nextFact != condition.Fact)
                {
                    condition = CreateDefaultCondition(nextFact, isEffect);
                }

                condition = DrawConditionValue(condition, isEffect);
                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    conditions.RemoveAt(index--);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                EditorGUILayout.EndHorizontal();
                conditions[index] = condition;
            }

            using (new EditorGUI.DisabledScope(domain == null || domain.Facts.All(fact => fact == null)))
            {
                if (GUILayout.Button($"+ {title.TrimEnd('s')}", GUILayout.Width(150f)))
                {
                    var usedFacts = conditions.Where(item => item.IsValid).Select(item => item.Fact).ToHashSet();
                    var fact = domain.Facts.FirstOrDefault(item => item != null && !usedFacts.Contains(item)) ??
                               domain.Facts.FirstOrDefault(item => item != null);
                    conditions.Add(CreateDefaultCondition(fact, isEffect));
                }
            }

            var duplicate = conditions.Where(item => item.IsValid)
                .GroupBy(item => item.Fact)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                EditorGUILayout.HelpBox(
                    $"Fact '{duplicate.Key.DisplayName}' is used more than once in {title}.",
                    MessageType.Error);
            }

            var foreign = conditions.FirstOrDefault(condition =>
                condition.IsValid && (domain == null || !domain.Facts.Contains(condition.Fact)));
            if (foreign.IsValid)
            {
                EditorGUILayout.HelpBox(
                    $"Fact '{foreign.Fact.DisplayName}' belongs to another Domain.",
                    MessageType.Error);
            }
        }

        public static GoapCondition CreateDefaultCondition(GoapFact fact, bool isEffect)
        {
            if (fact == null)
            {
                return default;
            }

            return fact.ValueType switch
            {
                GoapFactType.Integer => new GoapCondition(fact, fact.DefaultTypedValue.Integer),
                GoapFactType.Float => new GoapCondition(fact, fact.DefaultTypedValue.Float),
                GoapFactType.Enum => new GoapCondition(fact, fact.DefaultTypedValue.Integer),
                _ => new GoapCondition(fact, isEffect || !fact.DefaultValue)
            };
        }

        public static bool CanSubmit(
            IEnumerable<GoapCondition> conditions,
            GoapDomain domain,
            bool requireAtLeastOne)
        {
            var values = conditions?.ToArray() ?? System.Array.Empty<GoapCondition>();
            return (!requireAtLeastOne || values.Length > 0) &&
                   values.All(condition => condition.IsValid) &&
                   domain != null &&
                   values.All(condition => domain.Facts.Contains(condition.Fact)) &&
                   values.GroupBy(condition => condition.Fact).All(group => group.Count() == 1);
        }

        private static GoapCondition DrawConditionValue(GoapCondition condition, bool isEffect)
        {
            var fact = condition.Fact;
            if (fact == null)
            {
                GUILayout.Label("Select Fact", EditorStyles.centeredGreyMiniLabel, GUILayout.MinWidth(180f));
                return condition;
            }

            var comparison = isEffect ? GoapComparison.Equal : condition.Comparison;
            var operation = condition.EffectOperation;
            if (isEffect)
            {
                if (fact.ValueType == GoapFactType.Integer || fact.ValueType == GoapFactType.Float)
                {
                    operation = (GoapEffectOperation)EditorGUILayout.EnumPopup(operation, GUILayout.Width(90f));
                }
                else
                {
                    GUILayout.Label("Set", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(90f));
                    operation = GoapEffectOperation.Set;
                }
            }
            else if (fact.ValueType == GoapFactType.Boolean)
            {
                GUILayout.Label("Equal", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(90f));
                comparison = GoapComparison.Equal;
            }
            else if (fact.ValueType == GoapFactType.Enum)
            {
                var option = comparison == GoapComparison.NotEqual ? 1 : 0;
                option = EditorGUILayout.Popup(option, EqualityOptions, GUILayout.Width(90f));
                comparison = option == 0 ? GoapComparison.Equal : GoapComparison.NotEqual;
            }
            else
            {
                comparison = (GoapComparison)EditorGUILayout.EnumPopup(comparison, GUILayout.Width(90f));
            }

            switch (fact.ValueType)
            {
                case GoapFactType.Integer:
                    var integer = EditorGUILayout.IntField(condition.ExpectedValue.Integer, GUILayout.MinWidth(70f));
                    return new GoapCondition(fact, integer, comparison, operation);
                case GoapFactType.Float:
                    var floatValue = EditorGUILayout.FloatField(condition.ExpectedValue.Float, GUILayout.MinWidth(70f));
                    return new GoapCondition(fact, floatValue, comparison, operation);
                case GoapFactType.Enum:
                    var options = fact.EnumOptions.ToArray();
                    var enumValue = EditorGUILayout.Popup(
                        fact.NormalizeEnumIndex(condition.ExpectedValue.Integer),
                        options,
                        GUILayout.MinWidth(70f));
                    return new GoapCondition(fact, enumValue, comparison, GoapEffectOperation.Set);
                default:
                    var boolean = EditorGUILayout.Toggle(condition.ExpectedValue.Boolean, GUILayout.Width(20f));
                    GUILayout.Label(boolean ? "True" : "False", GUILayout.MinWidth(48f));
                    return new GoapCondition(fact, boolean);
            }
        }
    }
}
