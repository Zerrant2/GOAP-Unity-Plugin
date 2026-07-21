using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP.Editor
{
    public sealed class GoapProfileAnalysis
    {
        public IReadOnlyList<GoapGoalDefinition> Goals { get; }
        public IReadOnlyList<GoapActionDefinition> Actions { get; }
        public IReadOnlyList<GoapFactValueReference> InitialFacts { get; }
        public IReadOnlyList<GoapProfileSensorDefinition> Sensors { get; }
        public IReadOnlyList<GoapCondition> UnreachableConditions { get; }
        public IReadOnlyList<GoapFact> UnresolvedFacts { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool RequiresInventory { get; }
        public bool CanCreateProfile => Goals.Count > 0 && Actions.Count > 0 && UnreachableConditions.Count == 0;

        public GoapProfileAnalysis(
            IEnumerable<GoapGoalDefinition> goals,
            IEnumerable<GoapActionDefinition> actions,
            IEnumerable<GoapFactValueReference> initialFacts,
            IEnumerable<GoapProfileSensorDefinition> sensors,
            IEnumerable<GoapCondition> unreachableConditions,
            IEnumerable<GoapFact> unresolvedFacts,
            IEnumerable<string> warnings,
            bool requiresInventory)
        {
            Goals = goals?.Where(item => item != null).Distinct().ToArray() ?? Array.Empty<GoapGoalDefinition>();
            Actions = actions?.Where(item => item != null).Distinct().ToArray() ?? Array.Empty<GoapActionDefinition>();
            InitialFacts = initialFacts?.Where(item => item.IsValid).ToArray() ?? Array.Empty<GoapFactValueReference>();
            Sensors = sensors?.Where(item => item != null).ToArray() ?? Array.Empty<GoapProfileSensorDefinition>();
            UnreachableConditions = unreachableConditions?.Where(item => item.IsValid).Distinct().ToArray() ??
                                    Array.Empty<GoapCondition>();
            UnresolvedFacts = unresolvedFacts?.Where(item => item != null).Distinct().ToArray() ?? Array.Empty<GoapFact>();
            Warnings = warnings?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToArray() ?? Array.Empty<string>();
            RequiresInventory = requiresInventory;
        }
    }

    public static class GoapProfileComposer
    {
        public static GoapProfileAnalysis Analyze(
            GoapDomain domain,
            IEnumerable<GoapGoalDefinition> goals,
            bool includeAlternativeProducers = false)
        {
            if (domain == null)
            {
                return Empty("Assign a Domain to compose a profile.");
            }

            var selectedGoals = goals?.Where(goal => goal != null).Distinct().ToArray() ??
                                Array.Empty<GoapGoalDefinition>();
            var foreignGoal = selectedGoals.FirstOrDefault(goal => !domain.Goals.Contains(goal));
            if (foreignGoal != null)
            {
                return Empty($"Goal '{foreignGoal.DisplayName}' belongs to another Domain.");
            }

            var warnings = new List<string>();
            if (selectedGoals.Length == 0)
            {
                warnings.Add("Select at least one Goal.");
            }

            foreach (var goal in selectedGoals.Where(goal => goal.DesiredState.All(condition => !condition.IsValid)))
            {
                warnings.Add($"Goal '{goal.DisplayName}' has no valid Desired State.");
            }

            var initialValues = BuildInitialValues(selectedGoals, warnings);
            var includedActions = new HashSet<GoapActionDefinition>();
            var unreachable = new List<GoapCondition>();
            var externalRequirements = new List<Requirement>();
            var queue = new Queue<Requirement>();
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (var goal in selectedGoals)
            {
                foreach (var desired in goal.DesiredState.Where(condition => condition.IsValid))
                {
                    queue.Enqueue(new Requirement(desired, null));
                }
            }

            while (queue.Count > 0)
            {
                var requirement = queue.Dequeue();
                if (!visited.Add(requirement.Key) ||
                    requirement.Owner != null && MatchesBaseline(requirement.Condition, initialValues))
                {
                    continue;
                }

                var producers = domain.Actions
                    .Where(action => action != null &&
                                     action.Effects.Any(effect => effect.CanEstablish(requirement.Condition)))
                    .OrderBy(action => action.Cost)
                    .ThenBy(action => action.DisplayName, StringComparer.Ordinal)
                    .ToArray();
                if (producers.Length == 0)
                {
                    if (requirement.Owner == null)
                    {
                        unreachable.Add(requirement.Condition);
                    }
                    else
                    {
                        externalRequirements.Add(requirement);
                    }

                    continue;
                }

                var selectedProducers = includeAlternativeProducers ? producers : producers.Take(1);
                foreach (var producer in selectedProducers)
                {
                    if (!includedActions.Add(producer))
                    {
                        continue;
                    }

                    foreach (var precondition in producer.Preconditions.Where(condition => condition.IsValid))
                    {
                        queue.Enqueue(new Requirement(precondition, producer));
                    }
                }
            }

            var orderedActions = includedActions
                .OrderBy(action => domain.Actions.IndexOf(action))
                .ToArray();
            var sensors = InferSensors(orderedActions, externalRequirements, initialValues.Keys);
            AddUngroundedGoalConditions(
                selectedGoals,
                orderedActions,
                initialValues,
                externalRequirements,
                unreachable);
            var sensedFacts = sensors.Select(sensor => sensor.Fact).Where(fact => fact != null).ToHashSet();
            var unresolvedFacts = externalRequirements
                .Select(requirement => requirement.Condition.Fact)
                .Where(fact => fact != null && !initialValues.ContainsKey(fact) && !sensedFacts.Contains(fact))
                .Distinct()
                .ToArray();
            var requiresInventory = orderedActions.Any(action => action.ExecutionSteps.Any(step =>
                step != null &&
                (step.Kind == GoapActionStepKind.InventoryAdd || step.Kind == GoapActionStepKind.InventoryRemove)));

            return new GoapProfileAnalysis(
                selectedGoals,
                orderedActions,
                initialValues.Select(pair => CreateFactValue(pair.Key, pair.Value)),
                sensors,
                unreachable,
                unresolvedFacts,
                warnings,
                requiresInventory);
        }

        private static void AddUngroundedGoalConditions(
            IReadOnlyList<GoapGoalDefinition> goals,
            IReadOnlyList<GoapActionDefinition> actions,
            IReadOnlyDictionary<GoapFact, GoapValue> initialValues,
            IReadOnlyList<Requirement> externalRequirements,
            ICollection<GoapCondition> unreachable)
        {
            var externalFacts = externalRequirements
                .Select(requirement => requirement.Condition.Fact)
                .Where(fact => fact != null)
                .ToHashSet();
            var groundedActions = new HashSet<GoapActionDefinition>();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var action in actions.Where(action => !groundedActions.Contains(action)))
                {
                    var grounded = action.Preconditions
                        .Where(condition => condition.IsValid)
                        .All(condition =>
                            MatchesBaseline(condition, initialValues) ||
                            externalFacts.Contains(condition.Fact) ||
                            groundedActions.Any(producer =>
                                producer.Effects.Any(effect => effect.CanEstablish(condition))));
                    if (grounded)
                    {
                        changed |= groundedActions.Add(action);
                    }
                }
            }

            foreach (var desired in goals.SelectMany(goal => goal.DesiredState)
                         .Where(condition => condition.IsValid))
            {
                var hasGroundedProducer = groundedActions.Any(action =>
                    action.Effects.Any(effect => effect.CanEstablish(desired)));
                if (!hasGroundedProducer && !unreachable.Contains(desired))
                {
                    unreachable.Add(desired);
                }
            }
        }

        private static Dictionary<GoapFact, GoapValue> BuildInitialValues(
            IEnumerable<GoapGoalDefinition> goals,
            ICollection<string> warnings)
        {
            var result = new Dictionary<GoapFact, GoapValue>();
            foreach (var condition in goals.SelectMany(goal => goal.ActivationConditions)
                         .Where(condition => condition.IsValid))
            {
                var value = CreateMatchingValue(condition);
                if (result.TryGetValue(condition.Fact, out var existing) && !existing.Equals(value))
                {
                    warnings.Add(
                        $"Selected Goals require conflicting initial values for '{condition.Fact.DisplayName}'.");
                    continue;
                }

                result[condition.Fact] = value;
            }

            return result;
        }

        private static GoapProfileSensorDefinition[] InferSensors(
            IReadOnlyList<GoapActionDefinition> actions,
            IReadOnlyList<Requirement> externalRequirements,
            IEnumerable<GoapFact> initialFacts)
        {
            var initial = initialFacts.ToHashSet();
            var sensors = new Dictionary<GoapFact, GoapProfileSensorDefinition>();
            foreach (var requirement in externalRequirements)
            {
                var fact = requirement.Condition.Fact;
                if (fact == null || initial.Contains(fact) || sensors.ContainsKey(fact) ||
                    !LooksLikeWorldAvailability(fact.DisplayName))
                {
                    continue;
                }

                var categories = requirement.Owner.ExecutionSteps
                    .Where(step => step != null && step.Kind == GoapActionStepKind.FindSmartObject)
                    .Select(step => step.TargetCategory)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (categories.Length == 1)
                {
                    var sensor = new GoapProfileSensorDefinition(
                        $"{fact.DisplayName} Sensor",
                        GoapProfileSensorKind.SmartObject,
                        fact,
                        categories[0]);
                    sensor.ConfigureRange(0f);
                    sensors.Add(fact, sensor);
                }
            }

            foreach (var action in actions)
            {
                var itemIds = action.ExecutionSteps
                    .Where(step => step != null &&
                                   (step.Kind == GoapActionStepKind.InventoryAdd ||
                                    step.Kind == GoapActionStepKind.InventoryRemove))
                    .Select(step => step.ItemId)
                    .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var inventoryFacts = action.Effects
                    .Select(effect => effect.Fact)
                    .Where(fact => fact != null &&
                                   (fact.ValueType == GoapFactType.Integer || fact.ValueType == GoapFactType.Float))
                    .Distinct()
                    .ToArray();
                if (itemIds.Length != 1 || inventoryFacts.Length != 1 || sensors.ContainsKey(inventoryFacts[0]))
                {
                    continue;
                }

                sensors.Add(
                    inventoryFacts[0],
                    new GoapProfileSensorDefinition(
                        $"{inventoryFacts[0].DisplayName} Inventory",
                        GoapProfileSensorKind.Inventory,
                        inventoryFacts[0],
                        itemIds[0]));
            }

            return sensors.Values.OrderBy(sensor => sensor.Name, StringComparer.Ordinal).ToArray();
        }

        private static bool MatchesBaseline(
            GoapCondition condition,
            IReadOnlyDictionary<GoapFact, GoapValue> initialValues)
        {
            var value = initialValues.TryGetValue(condition.Fact, out var initial)
                ? initial
                : condition.Fact.DefaultTypedValue;
            return condition.Matches(value);
        }

        private static bool LooksLikeWorldAvailability(string displayName)
        {
            return displayName.IndexOf("available", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   displayName.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   displayName.IndexOf("nearby", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   displayName.IndexOf("in range", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GoapValue CreateMatchingValue(GoapCondition condition)
        {
            var expected = condition.ExpectedValue;
            if (condition.Fact.ValueType == GoapFactType.Boolean)
            {
                return GoapValue.From(condition.Comparison == GoapComparison.NotEqual
                    ? !expected.Boolean
                    : expected.Boolean);
            }

            if (condition.Fact.ValueType == GoapFactType.Float)
            {
                var value = expected.Float;
                value += condition.Comparison == GoapComparison.Greater ? 1f :
                    condition.Comparison == GoapComparison.Less ? -1f :
                    condition.Comparison == GoapComparison.NotEqual ? 1f : 0f;
                return GoapValue.From(value);
            }

            var integer = expected.Integer;
            integer += condition.Comparison == GoapComparison.Greater ? 1 :
                condition.Comparison == GoapComparison.Less ? -1 :
                condition.Comparison == GoapComparison.NotEqual ? 1 : 0;
            return condition.Fact.ValueType == GoapFactType.Enum
                ? GoapValue.FromEnum(condition.Fact.NormalizeEnumIndex(integer))
                : GoapValue.From(integer);
        }

        private static GoapFactValueReference CreateFactValue(GoapFact fact, GoapValue value)
        {
            return fact.ValueType switch
            {
                GoapFactType.Integer => new GoapFactValueReference(fact, value.Integer),
                GoapFactType.Float => new GoapFactValueReference(fact, value.Float),
                GoapFactType.Enum => new GoapFactValueReference(fact, value.Integer, true),
                _ => new GoapFactValueReference(fact, value.Boolean)
            };
        }

        private static GoapProfileAnalysis Empty(string warning)
        {
            return new GoapProfileAnalysis(null, null, null, null, null, null, new[] { warning }, false);
        }

        private readonly struct Requirement
        {
            public GoapCondition Condition { get; }
            public GoapActionDefinition Owner { get; }
            public string Key =>
                $"{Owner?.Id ?? "goal"}|{Condition.Fact?.Id ?? "fact"}|{Condition.Comparison}|{Condition.ExpectedValue.ToKeyString()}";

            public Requirement(GoapCondition condition, GoapActionDefinition owner)
            {
                Condition = condition;
                Owner = owner;
            }
        }
    }

    internal static class GoapReadOnlyListExtensions
    {
        public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (EqualityComparer<T>.Default.Equals(values[index], value))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }
    }
}
