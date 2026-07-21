using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP.Editor
{
    public enum GoapProfileInputSourceKind
    {
        Action,
        Sensor,
        InitialFact,
        DefaultValue,
        Missing
    }

    public sealed class GoapProfileCoverageEntry
    {
        public GoapCondition Condition { get; }
        public GoapDefinition Owner { get; }
        public GoapProfileInputSourceKind SourceKind { get; }
        public string SourceName { get; }
        public bool IsCovered => SourceKind != GoapProfileInputSourceKind.Missing;

        public GoapProfileCoverageEntry(
            GoapCondition condition,
            GoapDefinition owner,
            GoapProfileInputSourceKind sourceKind,
            string sourceName)
        {
            Condition = condition;
            Owner = owner;
            SourceKind = sourceKind;
            SourceName = sourceName ?? string.Empty;
        }
    }

    public sealed class GoapProfileCoverageReport
    {
        public IReadOnlyList<GoapProfileCoverageEntry> Entries { get; }
        public IReadOnlyList<GoapFact> MissingFacts { get; }
        public bool RequiresInventory { get; }
        public bool RequiresStats { get; }
        public bool RequiresNamedTargets { get; }
        public bool IsComplete => MissingFacts.Count == 0;

        public GoapProfileCoverageReport(
            IEnumerable<GoapProfileCoverageEntry> entries,
            bool requiresInventory,
            bool requiresStats,
            bool requiresNamedTargets)
        {
            Entries = entries?.ToArray() ?? Array.Empty<GoapProfileCoverageEntry>();
            MissingFacts = Entries
                .Where(entry => !entry.IsCovered && entry.Condition.Fact != null)
                .Select(entry => entry.Condition.Fact)
                .Distinct()
                .ToArray();
            RequiresInventory = requiresInventory;
            RequiresStats = requiresStats;
            RequiresNamedTargets = requiresNamedTargets;
        }
    }

    public static class GoapProfileCoverageAnalyzer
    {
        public static GoapProfileCoverageReport Analyze(GoapAgentProfile profile)
        {
            if (profile == null || profile.Domain == null)
            {
                return new GoapProfileCoverageReport(null, false, false, false);
            }

            var actions = profile.Actions.Where(action => action != null).ToArray();
            var entries = new List<GoapProfileCoverageEntry>();
            foreach (var action in actions)
            {
                foreach (var condition in action.Preconditions.Where(condition => condition.IsValid))
                {
                    entries.Add(Resolve(profile, actions, condition, action));
                }
            }

            foreach (var goal in profile.Goals.Where(goal => goal != null))
            {
                foreach (var condition in goal.ActivationConditions.Where(condition => condition.IsValid))
                {
                    entries.Add(Resolve(profile, actions, condition, goal));
                }
            }

            var requiresInventory = profile.Sensors.Any(sensor =>
                                        sensor != null && sensor.Kind == GoapProfileSensorKind.Inventory) ||
                                    actions.Any(action => action.ExecutionSteps.Any(step =>
                                        step != null &&
                                        (step.Kind == GoapActionStepKind.InventoryAdd ||
                                         step.Kind == GoapActionStepKind.InventoryRemove)));
            var requiresStats = profile.Sensors.Any(sensor =>
                sensor != null && sensor.Kind == GoapProfileSensorKind.Stat);
            var requiresNamedTargets = profile.Sensors.Any(sensor =>
                                           sensor != null &&
                                           (sensor.Kind == GoapProfileSensorKind.Distance ||
                                            sensor.Kind == GoapProfileSensorKind.ComponentProperty) &&
                                           !string.IsNullOrWhiteSpace(sensor.TargetId)) ||
                                       actions.Any(action => action.ExecutionSteps.Any(step =>
                                           step != null &&
                                           step.Kind == GoapActionStepKind.MoveToTarget &&
                                           !string.IsNullOrWhiteSpace(step.TargetId)));

            return new GoapProfileCoverageReport(
                entries,
                requiresInventory,
                requiresStats,
                requiresNamedTargets);
        }

        private static GoapProfileCoverageEntry Resolve(
            GoapAgentProfile profile,
            IReadOnlyList<GoapActionDefinition> actions,
            GoapCondition condition,
            GoapDefinition owner)
        {
            var producer = actions.FirstOrDefault(action =>
                action != owner && action.Effects.Any(effect => effect.CanEstablish(condition)));
            if (producer != null)
            {
                return new GoapProfileCoverageEntry(
                    condition,
                    owner,
                    GoapProfileInputSourceKind.Action,
                    producer.DisplayName);
            }

            var sensor = profile.Sensors.FirstOrDefault(item =>
                item != null && item.Fact == condition.Fact);
            if (sensor != null)
            {
                return new GoapProfileCoverageEntry(
                    condition,
                    owner,
                    GoapProfileInputSourceKind.Sensor,
                    sensor.Name);
            }

            var initialFact = profile.InitialFacts.FirstOrDefault(value =>
                value.IsValid &&
                value.Fact == condition.Fact &&
                condition.Matches(value.Value));
            if (initialFact.IsValid)
            {
                return new GoapProfileCoverageEntry(
                    condition,
                    owner,
                    GoapProfileInputSourceKind.InitialFact,
                    condition.Fact.FormatValue(initialFact.Value));
            }

            if (condition.Matches(condition.Fact.DefaultTypedValue))
            {
                return new GoapProfileCoverageEntry(
                    condition,
                    owner,
                    GoapProfileInputSourceKind.DefaultValue,
                    condition.Fact.FormatValue(condition.Fact.DefaultTypedValue));
            }

            return new GoapProfileCoverageEntry(
                condition,
                owner,
                GoapProfileInputSourceKind.Missing,
                "Missing provider");
        }
    }
}
