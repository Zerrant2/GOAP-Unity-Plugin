using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public enum GoapTraceEventType
    {
        Initialized,
        GoalSelected,
        PlanBuilt,
        PlanFailed,
        ActionStarted,
        ActionSucceeded,
        ActionFailed,
        GoalCompleted,
        Cancelled,
        SnapshotCaptured,
        SnapshotRestored
    }

    public readonly struct GoapTraceEntry
    {
        public float Time { get; }
        public GoapTraceEventType Type { get; }
        public string Message { get; }

        public GoapTraceEntry(float time, GoapTraceEventType type, string message)
        {
            Time = time;
            Type = type;
            Message = message;
        }
    }

    public readonly struct GoapConditionDiagnostic
    {
        public GoapCondition Condition { get; }
        public GoapValue ActualValue { get; }
        public bool Satisfied { get; }
        public string Requirement { get; }
        public string Actual { get; }
        public string Reason => Satisfied ? "Satisfied" : $"Expected {Requirement}; actual {Actual}";

        public GoapConditionDiagnostic(GoapCondition condition, GoapValue actualValue)
        {
            Condition = condition;
            ActualValue = actualValue;
            Satisfied = condition.IsValid && condition.Matches(actualValue);
            if (!condition.IsValid)
            {
                Requirement = "<missing fact>";
                Actual = "n/a";
                return;
            }

            var expected = condition.Fact.FormatValue(condition.ExpectedValue);
            var comparison = condition.Comparison switch
            {
                GoapComparison.Equal => "=",
                GoapComparison.NotEqual => "!=",
                GoapComparison.Less => "<",
                GoapComparison.LessOrEqual => "<=",
                GoapComparison.Greater => ">",
                GoapComparison.GreaterOrEqual => ">=",
                _ => "?"
            };
            Requirement = $"{condition.Fact.DisplayName} {comparison} {expected}";
            Actual = condition.Fact.FormatValue(actualValue);
        }
    }

    public readonly struct GoapActionDiagnostic
    {
        private readonly IReadOnlyList<GoapConditionDiagnostic> _preconditions;

        public GoapActionDefinition Action { get; }
        public bool HasExecutor { get; }
        public bool PreconditionsSatisfied { get; }
        public bool Executable => HasExecutor && PreconditionsSatisfied;
        public string Reason { get; }
        public IReadOnlyList<GoapConditionDiagnostic> Preconditions =>
            _preconditions ?? Array.Empty<GoapConditionDiagnostic>();

        public GoapActionDiagnostic(
            GoapActionDefinition action,
            bool hasExecutor,
            IEnumerable<GoapConditionDiagnostic> preconditions)
        {
            Action = action;
            HasExecutor = hasExecutor;
            _preconditions = (preconditions ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            PreconditionsSatisfied = _preconditions.All(item => item.Satisfied);
            if (!hasExecutor)
            {
                Reason = "No matching executor";
                return;
            }

            var unmet = _preconditions.Where(item => !item.Satisfied).ToArray();
            Reason = unmet.Length == 0
                ? "Ready now"
                : $"{unmet.Length} unmet: {unmet[0].Reason}";
        }
    }

    public readonly struct GoapGoalDiagnostic
    {
        private readonly IReadOnlyList<GoapConditionDiagnostic> _activationConditions;
        private readonly IReadOnlyList<GoapConditionDiagnostic> _desiredState;

        public GoapGoalDefinition Goal { get; }
        public bool Active { get; }
        public bool Satisfied { get; }
        public string Reason { get; }
        public IReadOnlyList<GoapConditionDiagnostic> ActivationConditions =>
            _activationConditions ?? Array.Empty<GoapConditionDiagnostic>();
        public IReadOnlyList<GoapConditionDiagnostic> DesiredState =>
            _desiredState ?? Array.Empty<GoapConditionDiagnostic>();

        public GoapGoalDiagnostic(
            GoapGoalDefinition goal,
            IEnumerable<GoapConditionDiagnostic> activationConditions,
            IEnumerable<GoapConditionDiagnostic> desiredState)
        {
            Goal = goal;
            _activationConditions = (activationConditions ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            _desiredState = (desiredState ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            Active = _activationConditions.All(item => item.Satisfied);
            Satisfied = _desiredState.All(item => item.Satisfied);

            if (Satisfied)
            {
                Reason = "Satisfied";
            }
            else if (Active)
            {
                Reason = "Active";
            }
            else
            {
                var unmet = _activationConditions.First(item => !item.Satisfied);
                Reason = $"Inactive: {unmet.Reason}";
            }
        }
    }

    public sealed class GoapDecisionSnapshot
    {
        private readonly GoapWorldState _worldState;
        private readonly IReadOnlyList<GoapActionDefinition> _planActions;
        private readonly IReadOnlyList<GoapActionDiagnostic> _actionDiagnostics;
        private readonly IReadOnlyList<GoapGoalDiagnostic> _goalDiagnostics;

        public int Sequence { get; }
        public float Time { get; }
        public GoapTraceEventType Trigger { get; }
        public string Reason { get; }
        public string Status { get; }
        public GoapDomain Domain { get; }
        public GoapGoalDefinition Goal { get; }
        public GoapActionDefinition Action { get; }
        public string PlanSummary { get; }
        public bool HasPlan { get; }
        public float PlanCost { get; }
        public int ExpandedStates { get; }
        public double PlanningMilliseconds { get; }
        public GoapPlanFailure PlanningFailure { get; }
        public string PlanningMessage { get; }
        public IReadOnlyList<GoapActionDefinition> PlanActions =>
            _planActions ?? Array.Empty<GoapActionDefinition>();
        public IReadOnlyList<GoapActionDiagnostic> ActionDiagnostics =>
            _actionDiagnostics ?? Array.Empty<GoapActionDiagnostic>();
        public IReadOnlyList<GoapGoalDiagnostic> GoalDiagnostics =>
            _goalDiagnostics ?? Array.Empty<GoapGoalDiagnostic>();

        internal GoapDecisionSnapshot(
            int sequence,
            float time,
            GoapTraceEventType trigger,
            string reason,
            string status,
            GoapDomain domain,
            GoapWorldState worldState,
            GoapGoalDefinition goal,
            GoapActionDefinition action,
            string planSummary,
            IEnumerable<GoapActionDefinition> planActions,
            GoapPlan plan,
            GoapPlanFailure planningFailure,
            string planningMessage,
            IEnumerable<GoapActionDiagnostic> actionDiagnostics,
            IEnumerable<GoapGoalDiagnostic> goalDiagnostics)
        {
            Sequence = sequence;
            Time = time;
            Trigger = trigger;
            Reason = reason ?? string.Empty;
            Status = status ?? string.Empty;
            Domain = domain;
            _worldState = worldState?.Clone();
            Goal = goal;
            Action = action;
            PlanSummary = planSummary ?? "No plan";
            _planActions = (planActions ?? Array.Empty<GoapActionDefinition>()).ToArray();
            HasPlan = plan != null;
            PlanCost = plan?.TotalCost ?? 0f;
            ExpandedStates = plan?.ExpandedStates ?? 0;
            PlanningMilliseconds = plan?.PlanningMilliseconds ?? 0d;
            PlanningFailure = planningFailure;
            PlanningMessage = planningMessage ?? string.Empty;
            _actionDiagnostics = (actionDiagnostics ?? Array.Empty<GoapActionDiagnostic>()).ToArray();
            _goalDiagnostics = (goalDiagnostics ?? Array.Empty<GoapGoalDiagnostic>()).ToArray();
        }

        public GoapValue GetValue(GoapFact fact)
        {
            return _worldState?.GetValue(fact) ?? GoapValue.From(false);
        }

        public GoapWorldState CaptureWorldState()
        {
            return _worldState?.Clone();
        }

        internal bool MatchesCurrent(
            GoapTraceEventType trigger,
            string status,
            GoapDomain domain,
            GoapWorldState worldState,
            GoapGoalDefinition goal,
            GoapActionDefinition action,
            string planSummary,
            GoapPlanFailure planningFailure,
            string planningMessage)
        {
            if (Trigger != trigger || Status != status || Domain != domain || Goal != goal || Action != action ||
                PlanSummary != planSummary || PlanningFailure != planningFailure || PlanningMessage != planningMessage ||
                _worldState == null || worldState == null)
            {
                return false;
            }

            return domain == null || domain.Facts
                .Where(fact => fact != null)
                .All(fact => _worldState.GetValue(fact).Equals(worldState.GetValue(fact)));
        }
    }

    public static class GoapDiagnosticUtility
    {
        public static GoapConditionDiagnostic EvaluateCondition(
            GoapCondition condition,
            GoapWorldState worldState)
        {
            var actual = condition.IsValid && worldState != null
                ? worldState.GetValue(condition.Fact)
                : GoapValue.From(false);
            return new GoapConditionDiagnostic(condition, actual);
        }

        public static GoapActionDiagnostic EvaluateAction(
            GoapActionDefinition action,
            GoapWorldState worldState,
            bool hasExecutor)
        {
            var conditions = action == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : action.Preconditions.Select(condition => EvaluateCondition(condition, worldState));
            return new GoapActionDiagnostic(action, hasExecutor, conditions);
        }

        public static GoapGoalDiagnostic EvaluateGoal(
            GoapGoalDefinition goal,
            GoapWorldState worldState)
        {
            var activation = goal == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : goal.ActivationConditions.Select(condition => EvaluateCondition(condition, worldState));
            var desired = goal == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : goal.DesiredState.Select(condition => EvaluateCondition(condition, worldState));
            return new GoapGoalDiagnostic(goal, activation, desired);
        }
    }
}
