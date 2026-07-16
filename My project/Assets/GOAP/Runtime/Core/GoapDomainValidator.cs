using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public enum GoapValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum GoapValidationFixKind
    {
        None,
        CreateExecutor,
        AddProducer,
        OpenSensor
    }

    public readonly struct GoapValidationIssue
    {
        public GoapValidationSeverity Severity { get; }
        public string Message { get; }
        public GoapDefinition Source { get; }
        public GoapValidationFixKind FixKind { get; }
        public GoapCondition RelatedCondition { get; }

        public GoapValidationIssue(
            GoapValidationSeverity severity,
            string message,
            GoapDefinition source = null,
            GoapValidationFixKind fixKind = GoapValidationFixKind.None,
            GoapCondition relatedCondition = default)
        {
            Severity = severity;
            Message = message;
            Source = source;
            FixKind = fixKind;
            RelatedCondition = relatedCondition;
        }
    }

    public static class GoapDomainValidator
    {
        public static IReadOnlyList<GoapValidationIssue> Validate(GoapDomain domain)
        {
            var issues = new List<GoapValidationIssue>();
            if (domain == null)
            {
                issues.Add(new GoapValidationIssue(GoapValidationSeverity.Error, "Domain is missing."));
                return issues;
            }

            var facts = domain.Facts.Where(fact => fact != null).ToHashSet();
            CheckNullEntries(domain, issues);
            CheckDuplicateIds(domain, issues);

            foreach (var fact in facts.Where(fact => fact.ValueType == GoapFactType.Enum))
            {
                if (fact.EnumOptions.Count == 0 || fact.EnumOptions.Any(string.IsNullOrWhiteSpace))
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Enum fact '{fact.DisplayName}' contains an empty option.",
                        fact));
                }

                if (fact.EnumOptions.Distinct(StringComparer.Ordinal).Count() != fact.EnumOptions.Count)
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Enum fact '{fact.DisplayName}' contains duplicate options.",
                        fact));
                }
            }

            if (domain.Goals.Count == 0)
            {
                issues.Add(new GoapValidationIssue(
                    GoapValidationSeverity.Warning,
                    "Domain has no goals."));
            }

            foreach (var action in domain.Actions.Where(action => action != null))
            {
                if (!action.UsesBuiltInExecutor && string.IsNullOrWhiteSpace(action.ExecutorId))
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Action '{action.DisplayName}' has no executor ID.",
                        action,
                        GoapValidationFixKind.CreateExecutor));
                }

                else if (action.UsesBuiltInExecutor &&
                         action.BuiltInExecution.Mode == GoapExecutionMode.SmartObjectInteraction &&
                         string.IsNullOrWhiteSpace(action.BuiltInExecution.TargetCategory))
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Built-in action '{action.DisplayName}' has no Smart Object category.",
                        action));
                }

                if (action.UsesBuiltInExecutor &&
                    action.BuiltInExecution.InventoryOperation != GoapInventoryOperation.None &&
                    string.IsNullOrWhiteSpace(action.BuiltInExecution.InventoryItemId))
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Built-in action '{action.DisplayName}' has an inventory operation without an item ID.",
                        action));
                }

                if (action.BuiltInExecution.Mode == GoapExecutionMode.Sequence)
                {
                    CheckExecutionSteps(action, issues);
                }

                if (action.Effects.Count == 0)
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Action '{action.DisplayName}' has no effects.",
                        action));
                }

                CheckConditions(action.Preconditions, facts, action, "precondition", false, issues);
                CheckConditions(action.Effects, facts, action, "effect", true, issues);

                foreach (var precondition in action.Preconditions.Where(condition => condition.IsValid))
                {
                    if (precondition.Matches(precondition.Fact.DefaultTypedValue))
                    {
                        continue;
                    }

                    var hasProducer = domain.Actions
                        .Where(candidate => candidate != null && candidate != action)
                        .Any(candidate => candidate.Effects.Any(effect => effect.CanEstablish(precondition)));
                    if (!hasProducer)
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Warning,
                            $"No action establishes precondition '{precondition}' for '{action.DisplayName}'. A sensor must provide it.",
                            action,
                            GoapValidationFixKind.OpenSensor,
                            precondition));
                    }
                }
            }

            foreach (var goal in domain.Goals.Where(goal => goal != null))
            {
                if (goal.DesiredState.Count == 0)
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Goal '{goal.DisplayName}' has no desired state.",
                        goal));
                    continue;
                }

                CheckConditions(goal.ActivationConditions, facts, goal, "activation condition", false, issues);
                CheckConditions(goal.DesiredState, facts, goal, "desired condition", false, issues);

                foreach (var desired in goal.DesiredState.Where(condition => condition.IsValid))
                {
                    var hasProducer = domain.Actions
                        .Where(action => action != null)
                        .Any(action => action.Effects.Any(effect => effect.CanEstablish(desired)));
                    if (!hasProducer && !desired.Matches(desired.Fact.DefaultTypedValue))
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Warning,
                            $"Nothing in this domain can establish '{desired}'.",
                            goal,
                            GoapValidationFixKind.AddProducer,
                            desired));
                    }
                }
            }


            foreach (var fact in facts)
            {
                var used = domain.Actions.Where(action => action != null)
                               .Any(action => action.Preconditions.Any(condition => condition.Fact == fact) ||
                                              action.Effects.Any(condition => condition.Fact == fact)) ||
                           domain.Goals.Where(goal => goal != null)
                               .Any(goal => goal.ActivationConditions.Any(condition => condition.Fact == fact) ||
                                            goal.DesiredState.Any(condition => condition.Fact == fact));
                if (!used)
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Info,
                        $"Fact '{fact.DisplayName}' is not used by any action or goal.",
                        fact));
                }
            }

            return issues;
        }

        private static void CheckExecutionSteps(
            GoapActionDefinition action,
            ICollection<GoapValidationIssue> issues)
        {
            if (action.ExecutionSteps.Count == 0)
            {
                issues.Add(new GoapValidationIssue(
                    GoapValidationSeverity.Error,
                    $"Sequence action '{action.DisplayName}' has no execution steps.",
                    action));
                return;
            }

            var hasTarget = false;
            foreach (var step in action.ExecutionSteps.Where(step => step != null))
            {
                switch (step.Kind)
                {
                    case GoapActionStepKind.FindSmartObject:
                        hasTarget = !string.IsNullOrWhiteSpace(step.TargetCategory);
                        if (!hasTarget)
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' has a Find Smart Object step without a category.",
                                action));
                        }
                        break;
                    case GoapActionStepKind.ReserveTarget:
                    case GoapActionStepKind.Interact:
                    case GoapActionStepKind.ConsumeTarget:
                        if (!hasTarget)
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' uses a target before Find Smart Object.",
                                action));
                        }
                        break;
                    case GoapActionStepKind.MoveToTarget:
                        if (!hasTarget && string.IsNullOrWhiteSpace(step.TargetId))
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' has a Move step without a found or named target.",
                                action));
                        }
                        break;
                    case GoapActionStepKind.InventoryAdd:
                    case GoapActionStepKind.InventoryRemove:
                        if (string.IsNullOrWhiteSpace(step.ItemId))
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' has an inventory step without an item ID.",
                                action));
                        }
                        break;
                    case GoapActionStepKind.SetFact:
                    case GoapActionStepKind.AddFact:
                    case GoapActionStepKind.SubtractFact:
                        if (!step.FactValue.IsValid)
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' has a fact step without a fact.",
                                action));
                        }
                        break;
                    case GoapActionStepKind.TriggerAnimation:
                    case GoapActionStepKind.InvokeEvent:
                        if (string.IsNullOrWhiteSpace(step.EventId))
                        {
                            issues.Add(new GoapValidationIssue(
                                GoapValidationSeverity.Error,
                                $"'{action.DisplayName}' has an event step without an ID.",
                                action));
                        }
                        break;
                }
            }
        }

        private static void CheckNullEntries(GoapDomain domain, ICollection<GoapValidationIssue> issues)
        {
            if (domain.Facts.Any(fact => fact == null) ||
                domain.Actions.Any(action => action == null) ||
                domain.Goals.Any(goal => goal == null))
            {
                issues.Add(new GoapValidationIssue(
                    GoapValidationSeverity.Warning,
                    "Domain contains missing asset references."));
            }
        }

        private static void CheckDuplicateIds(GoapDomain domain, ICollection<GoapValidationIssue> issues)
        {
            var definitions = domain.Facts.Cast<GoapDefinition>()
                .Concat(domain.Actions)
                .Concat(domain.Goals)
                .Where(definition => definition != null);

            foreach (var group in definitions.GroupBy(definition => definition.Id).Where(group => group.Count() > 1))
            {
                issues.Add(new GoapValidationIssue(
                    GoapValidationSeverity.Error,
                    $"{group.Count()} definitions share ID '{group.Key}'. Recreate one of the duplicated sub-assets."));
            }
        }

        private static void CheckConditions(
            IReadOnlyList<GoapCondition> conditions,
            HashSet<GoapFact> domainFacts,
            GoapDefinition source,
            string label,
            bool isEffect,
            ICollection<GoapValidationIssue> issues)
        {
            var seenFacts = new Dictionary<GoapFact, GoapCondition>();
            foreach (var condition in conditions)
            {
                if (!condition.IsValid)
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"'{source.DisplayName}' contains an empty {label}.",
                        source));
                    continue;
                }

                if (!domainFacts.Contains(condition.Fact))
                {
                    issues.Add(new GoapValidationIssue(
                        GoapValidationSeverity.Error,
                        $"Fact '{condition.Fact.DisplayName}' used by '{source.DisplayName}' is not in this domain.",
                        source));
                }


                if (condition.Fact.ValueType == GoapFactType.Boolean ||
                    condition.Fact.ValueType == GoapFactType.Enum)
                {
                    if (!isEffect && condition.Comparison != GoapComparison.Equal &&
                        condition.Comparison != GoapComparison.NotEqual)
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Error,
                            $"Fact '{condition.Fact.DisplayName}' only supports Equal and Not Equal comparisons.",
                            source));
                    }

                    if (isEffect && condition.EffectOperation != GoapEffectOperation.Set)
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Error,
                            $"Effect '{condition.Fact.DisplayName}' only supports Set.",
                            source));
                    }
                }

                if (seenFacts.TryGetValue(condition.Fact, out var previousCondition))
                {
                    if (previousCondition.Equals(condition))
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Error,
                            $"'{source.DisplayName}' has a duplicate {label} for '{condition.Fact.DisplayName}'.",
                            source));
                    }
                    else if (isEffect || condition.Fact.ValueType == GoapFactType.Boolean)
                    {
                        issues.Add(new GoapValidationIssue(
                            GoapValidationSeverity.Error,
                            $"'{source.DisplayName}' has contradictory {label}s for '{condition.Fact.DisplayName}'.",
                            source));
                    }
                }
                else
                {
                    seenFacts.Add(condition.Fact, condition);
                }
            }
        }
    }
}
