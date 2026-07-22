using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Profiling;

namespace Practice.GOAP
{
    [DisallowMultipleComponent]
    public sealed class GoapAgent : MonoBehaviour
    {
        private const int MaxTraceEntries = 100;
        private const int MaxDecisionSnapshots = 50;
        private static readonly ProfilerMarker DecisionMarker = new("GOAP.Agent.Decision");
        private static readonly ProfilerMarker SensorMarker = new("GOAP.Agent.Sensors");

        [SerializeField] private GoapDomain _domain;
        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField, Min(0.05f)] private float _decisionInterval = 0.2f;
        [SerializeField] private GoapPlannerSettings _plannerSettings;
        [SerializeField] private bool _logDecisions;

        private readonly GoapPlanner _planner = new();
        private readonly GoapGoalSelector _goalSelector = new();
        private readonly Queue<GoapActionDefinition> _pendingActions = new();
        private readonly List<GoapTraceEntry> _trace = new();
        private readonly List<GoapActionDiagnostic> _actionDiagnostics = new();
        private readonly List<GoapGoalDiagnostic> _goalDiagnostics = new();
        private readonly List<GoapDecisionSnapshot> _decisionSnapshots = new();
        private GoapActionBehaviour[] _actionBehaviours = Array.Empty<GoapActionBehaviour>();
        private GoapSensorBehaviour[] _sensors = Array.Empty<GoapSensorBehaviour>();
        private Coroutine _actionRoutine;
        private GoapActionBehaviour _runningBehaviour;
        private GoapActionContext _runningContext;
        private float _nextDecisionTime;
        private bool _initialized;
        private bool _replanRequested = true;
        private int _nextSnapshotSequence = 1;

        public GoapDomain Domain => _domain;
        public GoapAgentProfile Profile => _profile;
        public GoapWorldState WorldState { get; private set; }
        public GoapGoalDefinition CurrentGoal { get; private set; }
        public GoapGoalDefinition LastCompletedGoal { get; private set; }
        public GoapActionDefinition CurrentAction { get; private set; }
        public GoapPlan LastPlan { get; private set; }
        public GoapPlanFailure LastPlanningFailure { get; private set; }
        public string LastPlanningMessage { get; private set; } = string.Empty;
        public string StatusMessage { get; private set; } = "Waiting for domain";
        public bool PlanningDeferred { get; private set; }
        public bool Paused { get; private set; }
        public IReadOnlyCollection<GoapActionDefinition> PendingActions => _pendingActions;
        public IReadOnlyList<GoapTraceEntry> Trace => _trace;
        public IReadOnlyList<GoapActionDiagnostic> ActionDiagnostics => _actionDiagnostics;
        public IReadOnlyList<GoapGoalDiagnostic> GoalDiagnostics => _goalDiagnostics;
        public IReadOnlyList<GoapDecisionSnapshot> DecisionSnapshots => _decisionSnapshots;

        public event Action<GoapAgent> PlanChanged;
        public event Action<GoapAgent> ActionChanged;
        public event Action<GoapAgent, GoapGoalDefinition> GoalCompleted;

        private void Reset()
        {
            _plannerSettings = GoapPlannerSettings.Default;
        }

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            _replanRequested = true;
            _nextDecisionTime = 0f;
        }

        private void OnDisable()
        {
            GoapPlanningScheduler.Cancel(GetInstanceID());
            CancelCurrentAction("Agent disabled");
        }

        private void Update()
        {
            if (Paused || !EnsureInitialized() || Time.time < _nextDecisionTime)
            {
                return;
            }

            RunDecisionCycle();
        }

        public void Configure(GoapDomain domain, float decisionInterval = 0.2f, bool logDecisions = false)
        {
            _profile = null;
            ApplyConfiguration(domain, decisionInterval, GoapPlannerSettings.Default, logDecisions);
        }

        public void Configure(GoapAgentProfile profile)
        {
            _profile = profile;
            if (profile == null)
            {
                ApplyConfiguration(null, 0.2f, GoapPlannerSettings.Default, false);
                return;
            }

            ApplyConfiguration(
                profile.Domain,
                profile.DecisionInterval,
                profile.PlannerSettings,
                profile.LogDecisions);
        }

        public bool SetFact(GoapFact fact, bool value)
        {
            return SetFact(fact, GoapValue.From(value));
        }

        public bool SetFact(GoapFact fact, int value)
        {
            return SetFact(fact, GoapValue.From(value));
        }

        public bool SetFact(GoapFact fact, float value)
        {
            return SetFact(fact, GoapValue.From(value));
        }

        public bool SetFact(GoapFact fact, GoapValue value)
        {
            if (WorldState == null)
            {
                return false;
            }

            var changed = WorldState.SetValue(fact, value);
            if (changed)
            {
                _replanRequested = true;
            }

            return changed;
        }

        public void ForceDecision()
        {
            _replanRequested = true;
            _nextDecisionTime = 0f;
            RequestSensorRefresh();
        }

        public void RequestSensorRefresh()
        {
            foreach (var sensor in _sensors)
            {
                sensor?.RequestRefresh();
            }
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            if (paused)
            {
                GoapPlanningScheduler.Cancel(GetInstanceID());
                PlanningDeferred = false;
            }

            StatusMessage = paused ? "Paused by debugger" : "Decision loop resumed";
            if (!paused)
            {
                _nextDecisionTime = 0f;
            }
        }

        public void StepDecision()
        {
            if (EnsureInitialized())
            {
                RunDecisionCycle();
            }
        }

        public void StepAction()
        {
            StepDecision();
        }

        public GoapWorldState CaptureWorldState()
        {
            return WorldState?.Clone();
        }

        public GoapDecisionSnapshot CaptureDebugSnapshot(string reason = "Captured by debugger")
        {
            if (!EnsureInitialized())
            {
                return null;
            }

            AddTrace(GoapTraceEventType.SnapshotCaptured, reason);
            return _decisionSnapshots.Count > 0 ? _decisionSnapshots[^1] : null;
        }

        public bool RestoreDebugSnapshot(GoapDecisionSnapshot snapshot)
        {
            return snapshot != null && snapshot.Domain == _domain && RestoreWorldState(snapshot.CaptureWorldState());
        }

        public void ClearDebugHistory()
        {
            _trace.Clear();
            _decisionSnapshots.Clear();
        }

        public bool RestoreWorldState(GoapWorldState snapshot)
        {
            if (snapshot == null || !EnsureInitialized())
            {
                return false;
            }

            CancelCurrentAction("Restoring debugger snapshot");
            _pendingActions.Clear();
            CurrentGoal = null;
            LastPlan = null;
            LastPlanningFailure = GoapPlanFailure.None;
            LastPlanningMessage = string.Empty;
            WorldState = snapshot.Clone();
            _replanRequested = true;
            _nextDecisionTime = 0f;
            StatusMessage = "World state restored from snapshot";
            AddTrace(GoapTraceEventType.SnapshotRestored, StatusMessage);
            NotifyPlanChanged();
            return true;
        }

        public void AbortCurrentAction()
        {
            CancelCurrentAction("Action aborted by debugger");
            _pendingActions.Clear();
            _replanRequested = true;
            _nextDecisionTime = 0f;
            NotifyPlanChanged();
        }

        public string GetPlanSummary()
        {
            var names = new List<string>();
            if (CurrentAction != null)
            {
                names.Add($"> {CurrentAction.DisplayName}");
            }

            names.AddRange(_pendingActions.Select(action => action.DisplayName));
            return names.Count == 0 ? "No plan" : string.Join(" -> ", names);
        }

        private void ApplyConfiguration(
            GoapDomain domain,
            float decisionInterval,
            GoapPlannerSettings plannerSettings,
            bool logDecisions)
        {
            GoapPlanningScheduler.Cancel(GetInstanceID());
            CancelCurrentAction("Agent reconfigured");
            _pendingActions.Clear();
            _trace.Clear();
            _decisionSnapshots.Clear();
            _nextSnapshotSequence = 1;
            CurrentGoal = null;
            LastCompletedGoal = null;
            LastPlan = null;
            LastPlanningFailure = GoapPlanFailure.None;
            LastPlanningMessage = string.Empty;
            PlanningDeferred = false;
            _domain = domain;
            _decisionInterval = Mathf.Max(0.05f, decisionInterval);
            _logDecisions = logDecisions;
            _plannerSettings = plannerSettings;
            _initialized = false;
            _replanRequested = true;
        }

        private bool EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _initialized;
        }

        private void Initialize()
        {
            if (_profile != null)
            {
                _domain = _profile.Domain;
                _decisionInterval = _profile.DecisionInterval;
                _plannerSettings = _profile.PlannerSettings;
                _logDecisions = _profile.LogDecisions;
            }

            if (_domain == null)
            {
                StatusMessage = "GOAP domain is not assigned";
                return;
            }

            WorldState = _domain.CreateDefaultState();
            ApplyInitialFacts(_profile != null ? _profile.InitialFacts : null);
            if (TryGetComponent<GoapAgentAuthoring>(out var authoring))
            {
                ApplyInitialFacts(authoring.InitialFactOverrides);
            }

            RefreshComponents();
            RequestSensorRefresh();
            RefreshKnowledge(true);
            _plannerSettings = SanitizeSettings(_plannerSettings);
            _initialized = true;
            _replanRequested = true;
            _nextDecisionTime = 0f;
            StatusMessage = "Ready";
            AddTrace(GoapTraceEventType.Initialized, $"Initialized with domain '{_domain.name}'");
        }

        private void RefreshComponents()
        {
            _actionBehaviours = GetComponentsInChildren<GoapActionBehaviour>(true);
            _sensors = GetComponentsInChildren<GoapSensorBehaviour>(true);
        }

        private void ApplyInitialFacts(IEnumerable<GoapFactValueReference> values)
        {
            if (values == null)
            {
                return;
            }

            foreach (var value in values)
            {
                if (value.IsValid)
                {
                    WorldState.SetValue(value.Fact, value.Value);
                }
            }
        }

        private void RunDecisionCycle()
        {
            using var marker = DecisionMarker.Auto();
            _nextDecisionTime = Time.time + _decisionInterval;
            RefreshKnowledge(false);
            EvaluateGoalAndPlan();
            RefreshDiagnostics();
        }

        private void RefreshKnowledge(bool force)
        {
            using var marker = SensorMarker.Auto();
            foreach (var sensor in _sensors)
            {
                if (sensor != null && sensor.isActiveAndEnabled)
                {
                    sensor.TickSense(this, WorldState, force);
                }
            }
        }

        private void EvaluateGoalAndPlan()
        {
            if (CurrentGoal != null && WorldState.Satisfies(CurrentGoal.DesiredState))
            {
                CompleteGoal();
            }

            var selectedGoal = _goalSelector.Select(WorldState, GetAvailableGoals());
            if (selectedGoal != CurrentGoal)
            {
                CancelCurrentAction(selectedGoal == null ? "No active goal" : "Higher-priority goal selected");
                _pendingActions.Clear();
                CurrentGoal = selectedGoal;
                LastPlan = null;
                LastPlanningFailure = GoapPlanFailure.None;
                LastPlanningMessage = string.Empty;
                _replanRequested = true;
                if (selectedGoal != null)
                {
                    AddTrace(GoapTraceEventType.GoalSelected, $"Selected goal '{selectedGoal.DisplayName}'");
                }

                NotifyPlanChanged();
            }

            if (CurrentGoal == null)
            {
                GoapPlanningScheduler.Cancel(GetInstanceID());
                PlanningDeferred = false;
                StatusMessage = "Idle: every goal is satisfied";
                LastPlanningFailure = GoapPlanFailure.None;
                LastPlanningMessage = string.Empty;
                return;
            }

            if (_runningBehaviour != null)
            {
                if (!WorldState.Satisfies(CurrentAction.Preconditions) || !_runningBehaviour.CanContinue(_runningContext))
                {
                    CancelCurrentAction("Action invalidated by world state");
                    _pendingActions.Clear();
                    _replanRequested = true;
                }
                else
                {
                    return;
                }
            }

            if (_replanRequested || _pendingActions.Count == 0)
            {
                BuildPlan();
            }

            if (_runningBehaviour == null && _pendingActions.Count > 0)
            {
                StartNextAction();
            }
        }

        private void BuildPlan()
        {
            if (!GoapPlanningScheduler.TryAcquire(GetInstanceID()))
            {
                _pendingActions.Clear();
                PlanningDeferred = true;
                StatusMessage = "Planning queued: frame budget reached";
                return;
            }

            PlanningDeferred = false;
            _replanRequested = false;
            _pendingActions.Clear();

            var executableActions = GetAvailableActions()
                .Where(action => action != null && FindBehaviour(action) != null)
                .ToArray();
            var startedAt = Time.realtimeSinceStartupAsDouble;
            var result = _planner.PlanCompiled(
                WorldState,
                executableActions,
                CurrentGoal,
                _domain.Compile(),
                _plannerSettings);
            var planningMilliseconds = (Time.realtimeSinceStartupAsDouble - startedAt) * 1000d;
            GoapPlanningScheduler.Report(
                planningMilliseconds,
                result.Success,
                result.Plan?.ExpandedStates ?? 0);
            if (!result.Success)
            {
                LastPlan = null;
                LastPlanningFailure = result.Failure;
                LastPlanningMessage = result.Message;
                StatusMessage = $"No plan: {result.Message}";
                AddTrace(GoapTraceEventType.PlanFailed, StatusMessage);
                Log(StatusMessage);
                NotifyPlanChanged();
                return;
            }

            LastPlan = result.Plan;
            LastPlanningFailure = GoapPlanFailure.None;
            LastPlanningMessage = string.Empty;
            foreach (var action in result.Plan.Actions)
            {
                _pendingActions.Enqueue(action);
            }

            StatusMessage =
                $"Plan ready: {result.Plan.Actions.Count} actions, cost {result.Plan.TotalCost:0.##}, " +
                $"{result.Plan.ExpandedStates} states";
            AddTrace(
                GoapTraceEventType.PlanBuilt,
                $"{GetPlanSummary()} ({result.Plan.PlanningMilliseconds:0.###} ms)");
            Log($"Goal '{CurrentGoal.DisplayName}': {GetPlanSummary()}");
            NotifyPlanChanged();
        }

        private void StartNextAction()
        {
            var action = _pendingActions.Dequeue();
            var behaviour = FindBehaviour(action);
            var context = new GoapActionContext(this, action);

            string failureReason = null;
            if (behaviour == null)
            {
                failureReason = "no matching executor";
            }
            else if (!WorldState.Satisfies(action.Preconditions))
            {
                var unmet = action.Preconditions
                    .Select(condition => GoapDiagnosticUtility.EvaluateCondition(condition, WorldState))
                    .First(item => !item.Satisfied);
                failureReason = unmet.Reason;
            }
            else if (!behaviour.CanStart(context))
            {
                var executorDiagnostic = behaviour.EvaluateStart(context);
                failureReason = executorDiagnostic.CanStart
                    ? "executor rejected the scene state after the preflight check"
                    : executorDiagnostic.Message;
            }

            if (failureReason != null)
            {
                StatusMessage = $"Cannot start '{action.DisplayName}': {failureReason}; replanning";
                AddTrace(GoapTraceEventType.ActionFailed, StatusMessage);
                _pendingActions.Clear();
                _replanRequested = true;
                NotifyPlanChanged();
                return;
            }

            CurrentAction = action;
            _runningBehaviour = behaviour;
            _runningContext = context;
            StatusMessage = $"Executing: {action.DisplayName}";
            AddTrace(GoapTraceEventType.ActionStarted, action.DisplayName);
            ActionChanged?.Invoke(this);
            _actionRoutine = StartCoroutine(ExecuteCurrentAction());
        }

        private IEnumerator ExecuteCurrentAction()
        {
            yield return _runningBehaviour.Run(_runningContext);

            var completedAction = CurrentAction;
            var completedContext = _runningContext;
            var status = _runningBehaviour.Status;
            var failureReason = _runningBehaviour.LastFailureReason;
            _actionRoutine = null;
            _runningBehaviour = null;
            _runningContext = null;
            CurrentAction = null;

            if (status == GoapActionStatus.Succeeded)
            {
                completedContext?.ApplyStagedFacts();
                WorldState.Apply(completedAction.Effects.Where(
                    effect => completedContext == null || !completedContext.IsEffectHandled(effect.Fact)).ToArray());
                StatusMessage = $"Completed: {completedAction.DisplayName}";
                AddTrace(GoapTraceEventType.ActionSucceeded, completedAction.DisplayName);
                if (CurrentGoal != null && WorldState.Satisfies(CurrentGoal.DesiredState))
                {
                    CompleteGoal();
                }
            }
            else
            {
                _pendingActions.Clear();
                _replanRequested = true;
                StatusMessage = string.IsNullOrWhiteSpace(failureReason)
                    ? $"Failed: {completedAction.DisplayName}"
                    : $"Failed: {completedAction.DisplayName}: {failureReason}";
                AddTrace(GoapTraceEventType.ActionFailed, StatusMessage);
            }

            ActionChanged?.Invoke(this);
            NotifyPlanChanged();
            _nextDecisionTime = 0f;
        }

        private void CancelCurrentAction(string reason)
        {
            if (_runningBehaviour == null)
            {
                return;
            }

            _runningBehaviour.Cancel(_runningContext);
            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
            }

            _actionRoutine = null;
            _runningBehaviour = null;
            _runningContext = null;
            CurrentAction = null;
            StatusMessage = reason;
            AddTrace(GoapTraceEventType.Cancelled, reason);
            ActionChanged?.Invoke(this);
        }

        private void CompleteGoal()
        {
            var completedGoal = CurrentGoal;
            GoapPlanningScheduler.Cancel(GetInstanceID());
            PlanningDeferred = false;
            CancelCurrentAction($"Goal achieved: {completedGoal.DisplayName}");
            CurrentGoal = null;
            LastCompletedGoal = completedGoal;
            LastPlan = null;
            LastPlanningFailure = GoapPlanFailure.None;
            LastPlanningMessage = string.Empty;
            _pendingActions.Clear();
            StatusMessage = $"Goal achieved: {completedGoal.DisplayName}";
            AddTrace(GoapTraceEventType.GoalCompleted, completedGoal.DisplayName);
            Log(StatusMessage);
            GoalCompleted?.Invoke(this, completedGoal);
            NotifyPlanChanged();
        }

        private IReadOnlyList<GoapActionDefinition> GetAvailableActions()
        {
            return _profile != null ? _profile.Actions : _domain.Actions;
        }

        private IReadOnlyList<GoapGoalDefinition> GetAvailableGoals()
        {
            return _profile != null ? _profile.Goals : _domain.Goals;
        }

        private GoapActionBehaviour FindBehaviour(GoapActionDefinition action)
        {
            return _actionBehaviours.FirstOrDefault(behaviour => behaviour != null && behaviour.Supports(action));
        }

        private void RefreshDiagnostics()
        {
            _actionDiagnostics.Clear();
            if (_domain == null || WorldState == null)
            {
                _goalDiagnostics.Clear();
                return;
            }

            foreach (var action in GetAvailableActions().Where(action => action != null))
            {
                _actionDiagnostics.Add(EvaluateActionDiagnostic(action));
            }

            _goalDiagnostics.Clear();
            foreach (var goal in GetAvailableGoals().Where(goal => goal != null))
            {
                _goalDiagnostics.Add(GoapDiagnosticUtility.EvaluateGoal(goal, WorldState));
            }
        }

        private void NotifyPlanChanged()
        {
            PlanChanged?.Invoke(this);
        }

        private void AddTrace(GoapTraceEventType type, string message)
        {
            _trace.Add(new GoapTraceEntry(Time.time, type, message));
            if (_trace.Count > MaxTraceEntries)
            {
                _trace.RemoveAt(0);
            }

            CaptureDecisionSnapshot(type, message, type == GoapTraceEventType.SnapshotCaptured);
        }

        private void CaptureDecisionSnapshot(GoapTraceEventType trigger, string reason, bool force)
        {
            if (_domain == null || WorldState == null)
            {
                return;
            }

            var planSummary = GetPlanSummary();
            if (!force && _decisionSnapshots.Count > 0 && _decisionSnapshots[^1].MatchesCurrent(
                    trigger,
                    StatusMessage,
                    _domain,
                    WorldState,
                    CurrentGoal,
                    CurrentAction,
                    planSummary,
                    LastPlanningFailure,
                    LastPlanningMessage))
            {
                return;
            }

            var actions = GetAvailableActions()
                .Where(action => action != null)
                .Select(EvaluateActionDiagnostic)
                .ToArray();
            var goals = GetAvailableGoals()
                .Where(goal => goal != null)
                .Select(goal => GoapDiagnosticUtility.EvaluateGoal(goal, WorldState))
                .ToArray();
            var planActions = new List<GoapActionDefinition>();
            if (CurrentAction != null)
            {
                planActions.Add(CurrentAction);
            }

            planActions.AddRange(_pendingActions);
            _decisionSnapshots.Add(new GoapDecisionSnapshot(
                _nextSnapshotSequence++,
                Time.time,
                trigger,
                reason,
                StatusMessage,
                _domain,
                WorldState,
                CurrentGoal,
                CurrentAction,
                planSummary,
                planActions,
                LastPlan,
                LastPlanningFailure,
                LastPlanningMessage,
                actions,
                goals));
            if (_decisionSnapshots.Count > MaxDecisionSnapshots)
            {
                _decisionSnapshots.RemoveAt(0);
            }
        }

        private GoapActionDiagnostic EvaluateActionDiagnostic(GoapActionDefinition action)
        {
            var behaviour = FindBehaviour(action);
            if (behaviour == null)
            {
                return GoapDiagnosticUtility.EvaluateAction(action, WorldState, false);
            }

            var context = new GoapActionContext(this, action);
            return GoapDiagnosticUtility.EvaluateAction(
                action,
                WorldState,
                true,
                behaviour.EvaluateStart(context));
        }

        private void Log(string message)
        {
            if (_logDecisions)
            {
                Debug.Log($"[GOAP] {name}: {message}", this);
            }
        }

        private static GoapPlannerSettings SanitizeSettings(GoapPlannerSettings settings)
        {
            if (settings.MaxExpandedStates <= 0 || settings.MaxPlanDepth <= 0)
            {
                return GoapPlannerSettings.Default;
            }

            return settings;
        }
    }
}
