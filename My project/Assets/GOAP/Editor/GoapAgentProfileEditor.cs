using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomEditor(typeof(GoapAgentProfile))]
    public sealed class GoapAgentProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (GoapAgentProfile)target;
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Input Coverage", EditorStyles.boldLabel);

            if (profile.Domain == null)
            {
                EditorGUILayout.HelpBox("Assign a Domain before configuring inputs.", MessageType.Warning);
            }
            else
            {
                var report = GoapProfileCoverageAnalyzer.Analyze(profile);
                EditorGUILayout.HelpBox(
                    report.IsComplete
                        ? $"All {report.Entries.Count} Action and Goal inputs are covered."
                        : $"{report.MissingFacts.Count} Facts need a Sensor or Initial Fact.",
                    report.IsComplete ? MessageType.Info : MessageType.Warning);

                var missingEntries = report.Entries
                    .Where(entry => !entry.IsCovered)
                    .GroupBy(entry => entry.Condition.Fact)
                    .Select(group => group.First())
                    .Take(4);
                foreach (var entry in missingEntries)
                {
                    if (GUILayout.Button($"Configure {entry.Condition.Fact.DisplayName}"))
                    {
                        GoapContentWizardWindow.OpenSensors(
                            profile,
                            entry.Condition.Fact,
                            entry.Condition,
                            entry.Owner);
                    }
                }
            }

            if (GUILayout.Button("Open Sensor Builder", GUILayout.Height(28f)))
            {
                GoapContentWizardWindow.OpenSensors(profile);
            }
        }
    }
}
