using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public enum GoapTraceEventType
    {
        Initialized,
        GoalSelected,
        GoalSwitchDeferred,
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

    public enum GoapExecutorDiagnosticStatus
    {
        Ready,
        Warning,
        Blocked
    }

    public enum GoapExecutorIssueCode
    {
        None,
        MissingExecutor,
        ExecutorDisabled,
        InvalidConfiguration,
        SmartObjectNotFound,
        SmartObjectReserved,
        TargetMissing,
        InventoryMissing,
        InventoryInsufficient,
        NavMeshAgentMissing,
        NavMeshAgentDisabled,
        NavMeshNotReady,
        NavMeshPathInvalid,
        AnimatorMissing,
        AnimatorTriggerMissing,
        EventReceiverMissing,
        EventMissing,
        RequiredComponentMissing,
        Unknown
    }

    public readonly struct GoapExecutorDiagnostic
    {
        public GoapExecutorDiagnosticStatus Status { get; }
        public GoapExecutorIssueCode Code { get; }
        public string Message { get; }
        public bool CanStart => Status != GoapExecutorDiagnosticStatus.Blocked;

        private GoapExecutorDiagnostic(
            GoapExecutorDiagnosticStatus status,
            GoapExecutorIssueCode code,
            string message)
        {
            Status = status;
            Code = code;
            Message = string.IsNullOrWhiteSpace(message) ? "Ready now" : message;
        }

        public static GoapExecutorDiagnostic Ready(string message = "Ready now")
        {
            return new GoapExecutorDiagnostic(GoapExecutorDiagnosticStatus.Ready, GoapExecutorIssueCode.None, message);
        }

        public static GoapExecutorDiagnostic Warning(GoapExecutorIssueCode code, string message)
        {
            return new GoapExecutorDiagnostic(GoapExecutorDiagnosticStatus.Warning, code, message);
        }

        public static GoapExecutorDiagnostic Blocked(GoapExecutorIssueCode code, string message)
        {
            return new GoapExecutorDiagnostic(GoapExecutorDiagnosticStatus.Blocked, code, message);
        }
    }

    public readonly struct GoapActionDiagnostic
    {
        private readonly IReadOnlyList<GoapConditionDiagnostic> _preconditions;

        public GoapActionDefinition Action { get; }
        public bool HasExecutor { get; }
        public bool PreconditionsSatisfied { get; }
        public bool PlanningAvailable => !float.IsNaN(PlanningCost) && !float.IsInfinity(PlanningCost);
        public bool Executable => HasExecutor && PlanningAvailable && PreconditionsSatisfied && ExecutorDiagnostic.CanStart;
        public float BaseCost { get; }
        public float PlanningCost { get; }
        public string Reason { get; }
        public GoapExecutorDiagnostic ExecutorDiagnostic { get; }
        public IReadOnlyList<GoapConditionDiagnostic> Preconditions =>
            _preconditions ?? Array.Empty<GoapConditionDiagnostic>();

        public GoapActionDiagnostic(
            GoapActionDefinition action,
            bool hasExecutor,
            IEnumerable<GoapConditionDiagnostic> preconditions)
            : this(
                action,
                hasExecutor,
                hasExecutor
                    ? GoapExecutorDiagnostic.Ready()
                    : GoapExecutorDiagnostic.Blocked(
                        GoapExecutorIssueCode.MissingExecutor,
                        "No matching executor"),
                preconditions,
                null)
        {
        }

        public GoapActionDiagnostic(
            GoapActionDefinition action,
            bool hasExecutor,
            GoapExecutorDiagnostic executorDiagnostic,
            IEnumerable<GoapConditionDiagnostic> preconditions,
            float? planningCost = null)
        {
            Action = action;
            HasExecutor = hasExecutor;
            BaseCost = action?.Cost ?? 0f;
            PlanningCost = planningCost ?? BaseCost;
            ExecutorDiagnostic = executorDiagnostic;
            _preconditions = (preconditions ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            PreconditionsSatisfied = _preconditions.All(item => item.Satisfied);
            if (!hasExecutor)
            {
                Reason = "No matching executor";
                return;
            }

            if (float.IsNaN(PlanningCost) || float.IsInfinity(PlanningCost))
            {
                Reason = "Planning context is unavailable";
                return;
            }

            var unmet = _preconditions.Where(item => !item.Satisfied).ToArray();
            if (unmet.Length > 0)
            {
                Reason = $"{unmet.Length} unmet: {unmet[0].Reason}";
            }
            else
            {
                Reason = executorDiagnostic.Message;
            }
        }
    }

    public readonly struct GoapGoalDiagnostic
    {
        private readonly IReadOnlyList<GoapConditionDiagnostic> _activationConditions;
        private readonly IReadOnlyList<GoapConditionDiagnostic> _desiredState;
        private readonly IReadOnlyList<GoapGoalScoreTerm> _scoreTerms;

        public GoapGoalDefinition Goal { get; }
        public bool Active { get; }
        public bool Satisfied { get; }
        public bool Eligible { get; }
        public bool OnCooldown { get; }
        public float CooldownRemaining { get; }
        public float BaseScore { get; }
        public float ModifierScore { get; }
        public float FinalScore { get; }
        public string Reason { get; }
        public IReadOnlyList<GoapConditionDiagnostic> ActivationConditions =>
            _activationConditions ?? Array.Empty<GoapConditionDiagnostic>();
        public IReadOnlyList<GoapConditionDiagnostic> DesiredState =>
            _desiredState ?? Array.Empty<GoapConditionDiagnostic>();
        public IReadOnlyList<GoapGoalScoreTerm> ScoreTerms =>
            _scoreTerms ?? Array.Empty<GoapGoalScoreTerm>();

        public GoapGoalDiagnostic(
            GoapGoalDefinition goal,
            IEnumerable<GoapConditionDiagnostic> activationConditions,
            IEnumerable<GoapConditionDiagnostic> desiredState)
            : this(goal, activationConditions, desiredState, null)
        {
        }

        public GoapGoalDiagnostic(
            GoapGoalDefinition goal,
            IEnumerable<GoapConditionDiagnostic> activationConditions,
            IEnumerable<GoapConditionDiagnostic> desiredState,
            GoapGoalEvaluation evaluation)
        {
            Goal = goal;
            _activationConditions = (activationConditions ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            _desiredState = (desiredState ?? Array.Empty<GoapConditionDiagnostic>()).ToArray();
            Active = _activationConditions.All(item => item.Satisfied);
            Satisfied = _desiredState.All(item => item.Satisfied);
            BaseScore = evaluation?.BaseScore ?? goal?.Priority ?? 0f;
            ModifierScore = evaluation?.ModifierScore ?? 0f;
            FinalScore = evaluation?.FinalScore ?? BaseScore;
            CooldownRemaining = evaluation?.CooldownRemaining ?? 0f;
            OnCooldown = CooldownRemaining > 0f;
            Eligible = evaluation?.Eligible ?? (goal != null && Active && !Satisfied);
            _scoreTerms = evaluation?.ScoreTerms?.ToArray() ?? Array.Empty<GoapGoalScoreTerm>();

            if (Satisfied)
            {
                Reason = "Satisfied";
            }
            else if (OnCooldown)
            {
                Reason = $"Cooldown: {CooldownRemaining:0.0}s remaining";
            }
            else if (Active)
            {
                Reason = $"Eligible: score {FinalScore:0.##}";
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
            bool hasExecutor,
            float? planningCost = null)
        {
            return EvaluateAction(
                action,
                worldState,
                hasExecutor,
                hasExecutor
                    ? GoapExecutorDiagnostic.Ready()
                    : GoapExecutorDiagnostic.Blocked(
                        GoapExecutorIssueCode.MissingExecutor,
                        "No matching executor"),
                planningCost);
        }

        public static GoapActionDiagnostic EvaluateAction(
            GoapActionDefinition action,
            GoapWorldState worldState,
            bool hasExecutor,
            GoapExecutorDiagnostic executorDiagnostic,
            float? planningCost = null)
        {
            var conditions = action == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : action.Preconditions.Select(condition => EvaluateCondition(condition, worldState));
            return new GoapActionDiagnostic(action, hasExecutor, executorDiagnostic, conditions, planningCost);
        }

        public static GoapGoalDiagnostic EvaluateGoal(
            GoapGoalDefinition goal,
            GoapWorldState worldState,
            GoapGoalEvaluation evaluation = null)
        {
            var activation = goal == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : goal.ActivationConditions.Select(condition => EvaluateCondition(condition, worldState));
            var desired = goal == null
                ? Array.Empty<GoapConditionDiagnostic>()
                : goal.DesiredState.Select(condition => EvaluateCondition(condition, worldState));
            return new GoapGoalDiagnostic(goal, activation, desired, evaluation);
        }
    }
}
