using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Practice.GOAP.Editor
{
    public static class GoapEditorDomainValidator
    {
        public static IReadOnlyList<GoapValidationIssue> Validate(GoapDomain domain)
        {
            var issues = GoapDomainValidator.Validate(domain);
            if (domain == null)
            {
                return issues;
            }

            var profiles = AssetDatabase.FindAssets("t:GoapAgentProfile")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GoapAgentProfile>)
                .Where(profile => profile != null && profile.Domain == domain)
                .ToArray();
            if (profiles.Length == 0)
            {
                return issues;
            }

            var result = new List<GoapValidationIssue>();
            var unusedActions = new HashSet<GoapActionDefinition>();
            foreach (var issue in issues)
            {
                if (issue.Severity != GoapValidationSeverity.Warning ||
                    issue.FixKind != GoapValidationFixKind.OpenSensor ||
                    issue.Source is not GoapActionDefinition action ||
                    !issue.RelatedCondition.IsValid)
                {
                    result.Add(issue);
                    continue;
                }

                var relevantProfiles = profiles
                    .Where(profile => profile.Actions.Contains(action))
                    .ToArray();
                if (relevantProfiles.Length == 0)
                {
                    if (unusedActions.Add(action))
                    {
                        result.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Info,
                            $"Action '{action.DisplayName}' is not used by any Agent Profile; sensor coverage was not checked.",
                            action));
                    }

                    continue;
                }

                var missingProfiles = relevantProfiles
                    .Where(profile => !Provides(profile, issue.RelatedCondition))
                    .ToArray();
                if (missingProfiles.Length == 0)
                {
                    continue;
                }

                var profileNames = string.Join(", ", missingProfiles.Select(profile => profile.name));
                result.Add(new GoapValidationIssue(
                    GoapValidationSeverity.Warning,
                    $"Precondition '{issue.RelatedCondition}' for '{action.DisplayName}' has no producer or value provider in: {profileNames}.",
                    action,
                    GoapValidationFixKind.OpenSensor,
                    issue.RelatedCondition));
            }

            return result;
        }

        private static bool Provides(GoapAgentProfile profile, GoapCondition condition)
        {
            return profile.Sensors.Any(sensor => sensor != null && sensor.Fact == condition.Fact) ||
                   profile.InitialFacts.Any(value =>
                       value.IsValid &&
                       value.Fact == condition.Fact &&
                       condition.Matches(value.Value));
        }
    }
}
