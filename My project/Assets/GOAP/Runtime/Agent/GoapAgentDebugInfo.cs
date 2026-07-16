using System;

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

    public readonly struct GoapActionDiagnostic
    {
        public GoapActionDefinition Action { get; }
        public bool Executable { get; }
        public string Reason { get; }

        public GoapActionDiagnostic(GoapActionDefinition action, bool executable, string reason)
        {
            Action = action;
            Executable = executable;
            Reason = reason;
        }
    }

    public readonly struct GoapGoalDiagnostic
    {
        public GoapGoalDefinition Goal { get; }
        public bool Active { get; }
        public bool Satisfied { get; }
        public string Reason { get; }

        public GoapGoalDiagnostic(GoapGoalDefinition goal, bool active, bool satisfied, string reason)
        {
            Goal = goal;
            Active = active;
            Satisfied = satisfied;
            Reason = reason;
        }
    }
}
