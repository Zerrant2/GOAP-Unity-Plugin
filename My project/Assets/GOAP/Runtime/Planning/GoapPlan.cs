using System;
using System.Collections.Generic;

namespace Practice.GOAP
{
    public enum GoapPlanFailure
    {
        None,
        InvalidInput,
        GoalHasNoProducer,
        NoPlanFound,
        SearchLimitReached,
        TimeLimitReached,
        Cancelled
    }

    public sealed class GoapPlan
    {
        private readonly IReadOnlyList<GoapActionDefinition> _actions;

        public IReadOnlyList<GoapActionDefinition> Actions => _actions;
        public float TotalCost { get; }
        public int ExpandedStates { get; }
        public double PlanningMilliseconds { get; }

        internal GoapPlan(
            List<GoapActionDefinition> actions,
            float totalCost,
            int expandedStates,
            double planningMilliseconds = 0d)
        {
            _actions = actions.AsReadOnly();
            TotalCost = totalCost;
            ExpandedStates = expandedStates;
            PlanningMilliseconds = planningMilliseconds;
        }
    }

    public readonly struct GoapPlanResult
    {
        public bool Success { get; }
        public GoapPlan Plan { get; }
        public GoapPlanFailure Failure { get; }
        public string Message { get; }

        private GoapPlanResult(bool success, GoapPlan plan, GoapPlanFailure failure, string message)
        {
            Success = success;
            Plan = plan;
            Failure = failure;
            Message = message;
        }

        internal static GoapPlanResult Succeeded(GoapPlan plan)
        {
            return new GoapPlanResult(true, plan, GoapPlanFailure.None, string.Empty);
        }

        internal static GoapPlanResult Failed(GoapPlanFailure failure, string message)
        {
            return new GoapPlanResult(false, null, failure, message);
        }
    }

    [Serializable]
    public struct GoapPlannerSettings
    {
        public int MaxExpandedStates;
        public int MaxPlanDepth;
        public float MaxPlanningMilliseconds;

        public static GoapPlannerSettings Default => new()
        {
            MaxExpandedStates = 5000,
            MaxPlanDepth = 32,
            MaxPlanningMilliseconds = 10f
        };

        internal GoapPlannerSettings Sanitized()
        {
            return new GoapPlannerSettings
            {
                MaxExpandedStates = Math.Max(1, MaxExpandedStates),
                MaxPlanDepth = Math.Max(1, MaxPlanDepth),
                MaxPlanningMilliseconds = MaxPlanningMilliseconds <= 0f ? 10f : MaxPlanningMilliseconds
            };
        }
    }
}
