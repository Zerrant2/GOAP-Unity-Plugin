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
        private GoapActionBehaviour[] _actionBehaviours = Array.Empty<GoapActionBehaviour>();
        private GoapSensorBehaviour[] _sensors = Array.Empty<GoapSensorBehaviour>();
        private Coroutine _actionRoutine;
        private GoapActionBehaviour _runningBehaviour;
        private GoapActionContext _runningContext;
        private float _nextDecisionTime;
        private bool _initialized;
        private bool _replanRequested = true;

        public GoapDomain Domain => _domain;
        public GoapAgentProfile Profile => _profile;
        public GoapWorldState WorldState { get; private set; }
        public GoapGoalDefinition CurrentGoal { get; private set; }
        public GoapGoalDefinition LastCompletedGoal { get; private set; }
        public GoapActionDefinition CurrentAction { get; private set; }
        public GoapPlan LastPlan { get; private set; }
        public string StatusMessage { get; private set; } = "Waiting for domain";
        public bool Paused { get; private set; }
        public IReadOnlyCollection<GoapActionDefinition> PendingActions => _pendingActions;
        public IReadOnlyList<GoapTraceEntry> Trace => _trace;
        public IReadOnlyList<GoapActionDiagnostic> ActionDiagnostics => _actionDiagnostics;
        public IReadOnlyList<GoapGoalDiagnostic> GoalDiagnostics => _goalDiagnostics;

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
            CancelCurrentAction("Agent reconfigured");
            _pendingActions.Clear();
            _trace.Clear();
            CurrentGoal = null;
            LastCompletedGoal = null;
            LastPlan = null;
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
                _replanRequested = true;
                if (selectedGoal != null)
                {
                    AddTrace(GoapTraceEventType.GoalSelected, $"Selected goal '{selectedGoal.DisplayName}'");
                }

                NotifyPlanChanged();
            }

            if (CurrentGoal == null)
            {
                StatusMessage = "Idle: every goal is satisfied";
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
            _replanRequested = false;
            _pendingActions.Clear();

            var executableActions = GetAvailableActions()
                .Where(action => action != null && FindBehaviour(action) != null)
                .ToArray();
            var result = _planner.Plan(WorldState, executableActions, CurrentGoal, _plannerSettings);
            if (!result.Success)
            {
                LastPlan = null;
                StatusMessage = $"No plan: {result.Message}";
                AddTrace(GoapTraceEventType.PlanFailed, StatusMessage);
                Log(StatusMessage);
                NotifyPlanChanged();
                return;
            }

            LastPlan = result.Plan;
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

            if (behaviour == null || !WorldState.Satisfies(action.Preconditions) || !behaviour.CanStart(context))
            {
                StatusMessage = $"Cannot start '{action.DisplayName}', replanning";
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
                StatusMessage = $"Failed: {completedAction.DisplayName}";
                AddTrace(GoapTraceEventType.ActionFailed, completedAction.DisplayName);
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
            CancelCurrentAction($"Goal achieved: {completedGoal.DisplayName}");
            CurrentGoal = null;
            LastCompletedGoal = completedGoal;
            LastPlan = null;
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
                return;
            }

            foreach (var action in GetAvailableActions().Where(action => action != null))
            {
                var behaviour = FindBehaviour(action);
                if (behaviour == null)
                {
                    _actionDiagnostics.Add(new GoapActionDiagnostic(action, false, "No matching executor"));
                }
                else if (WorldState.Satisfies(action.Preconditions))
                {
                    _actionDiagnostics.Add(new GoapActionDiagnostic(action, true, "Ready now"));
                }
                else
                {
                    var missing = action.Preconditions
                        .Where(condition => !condition.Matches(WorldState.GetValue(condition.Fact)))
                        .Select(condition => condition.ToString());
                    _actionDiagnostics.Add(new GoapActionDiagnostic(
                        action,
                        true,
                        $"Waiting for: {string.Join(", ", missing)}"));
                }
            }

            _goalDiagnostics.Clear();
            foreach (var goal in GetAvailableGoals().Where(goal => goal != null))
            {
                var active = WorldState.Satisfies(goal.ActivationConditions);
                var satisfied = WorldState.Satisfies(goal.DesiredState);
                var reason = satisfied ? "Satisfied" : active ? "Active" : "Activation conditions are false";
                _goalDiagnostics.Add(new GoapGoalDiagnostic(goal, active, satisfied, reason));
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
