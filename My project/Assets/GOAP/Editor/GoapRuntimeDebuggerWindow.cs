using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    public sealed class GoapRuntimeDebuggerWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Overview", "Facts", "Goals", "Actions", "History" };

        private readonly Dictionary<int, bool> _actionFoldouts = new();
        private readonly Dictionary<int, bool> _goalFoldouts = new();
        private GoapAgent _agent;
        private GoapDecisionSnapshot _selectedSnapshot;
        private Vector2 _scroll;
        private bool _followSelection = true;
        private bool _blockedActionsOnly;
        private bool _showEventLog;
        private string _factSearch = string.Empty;
        private string _actionSearch = string.Empty;
        private int _selectedTab;
        private double _nextRepaint;

        private bool ViewingSnapshot => _selectedSnapshot != null;
        private GoapDomain ViewedDomain => ViewingSnapshot ? _selectedSnapshot.Domain : _agent?.Domain;
        private GoapGoalDefinition ViewedGoal => ViewingSnapshot ? _selectedSnapshot.Goal : _agent?.CurrentGoal;
        private GoapActionDefinition ViewedAction => ViewingSnapshot ? _selectedSnapshot.Action : _agent?.CurrentAction;
        private IReadOnlyList<GoapActionDiagnostic> ViewedActionDiagnostics => ViewingSnapshot
            ? _selectedSnapshot.ActionDiagnostics
            : _agent?.ActionDiagnostics ?? Array.Empty<GoapActionDiagnostic>();
        private IReadOnlyList<GoapGoalDiagnostic> ViewedGoalDiagnostics => ViewingSnapshot
            ? _selectedSnapshot.GoalDiagnostics
            : _agent?.GoalDiagnostics ?? Array.Empty<GoapGoalDiagnostic>();

        [MenuItem("Tools/GOAP/Runtime Debugger %#d")]
        public static void Open()
        {
            var window = GetWindow<GoapRuntimeDebuggerWindow>();
            window.titleContent = new GUIContent("GOAP Debugger");
            window.minSize = new Vector2(680f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += FollowSelection;
            EditorApplication.update += RepaintOnInterval;
            FollowSelection();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= FollowSelection;
            EditorApplication.update -= RepaintOnInterval;
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect live GOAP decisions.", MessageType.Info);
                return;
            }

            if (_agent == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with a GOAP Agent.", MessageType.Info);
                DrawAgentPicker();
                return;
            }

            ValidateSelectedSnapshot();
            DrawAgentHeader();
            _selectedTab = GUILayout.Toolbar(_selectedTab, TabNames);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_selectedTab)
            {
                case 0:
                    DrawOverview();
                    break;
                case 1:
                    DrawWorldState();
                    break;
                case 2:
                    DrawGoals();
                    break;
                case 3:
                    DrawActions();
                    break;
                case 4:
                    DrawHistory();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var selectedAgent = (GoapAgent)EditorGUILayout.ObjectField(
                _agent,
                typeof(GoapAgent),
                true,
                GUILayout.MinWidth(180f));
            if (selectedAgent != _agent)
            {
                SetAgent(selectedAgent);
            }

            _followSelection = GUILayout.Toggle(_followSelection, "Follow Selection", EditorStyles.toolbarButton);
            if (GUILayout.Button("Find", EditorStyles.toolbarButton, GUILayout.Width(46f)))
            {
                SetAgent(FindFirstObjectByType<GoapAgent>());
                if (_agent != null)
                {
                    Selection.activeGameObject = _agent.gameObject;
                }
            }

            if (ViewingSnapshot && GUILayout.Button("Return to Live", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                ReturnToLive();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAgentPicker()
        {
            foreach (var agent in FindObjectsByType<GoapAgent>(FindObjectsSortMode.None))
            {
                if (GUILayout.Button(agent.name))
                {
                    SetAgent(agent);
                    Selection.activeGameObject = agent.gameObject;
                }
            }
        }

        private void DrawAgentHeader()
        {
            var status = ViewingSnapshot ? _selectedSnapshot.Status : _agent.StatusMessage;
            var goal = ViewedGoal != null ? ViewedGoal.DisplayName : "None";
            var action = ViewedAction != null ? ViewedAction.DisplayName : "None";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_agent.name, EditorStyles.boldLabel, GUILayout.Width(180f));
            EditorGUILayout.LabelField($"Goal: {goal}", GUILayout.MinWidth(150f));
            EditorGUILayout.LabelField($"Action: {action}", GUILayout.MinWidth(150f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            if (ViewingSnapshot)
            {
                EditorGUILayout.HelpBox(
                    $"Viewing snapshot #{_selectedSnapshot.Sequence} at {_selectedSnapshot.Time:0.00}s " +
                    $"({_selectedSnapshot.Trigger}). The live agent is not frozen.",
                    MessageType.Info);
            }

            DrawLiveControls();
            EditorGUILayout.EndVertical();
        }

        private void DrawLiveControls()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_agent.Paused ? "Resume" : "Pause", GUILayout.Width(72f)))
            {
                _agent.SetPaused(!_agent.Paused);
            }

            if (GUILayout.Button("Step Action", GUILayout.Width(88f)))
            {
                _agent.StepAction();
            }

            if (GUILayout.Button("Force Replan", GUILayout.Width(92f)))
            {
                _agent.ForceDecision();
                _agent.StepDecision();
            }

            if (GUILayout.Button("Abort", GUILayout.Width(58f)))
            {
                _agent.AbortCurrentAction();
            }

            if (GUILayout.Button("Capture", GUILayout.Width(64f)))
            {
                SelectSnapshot(_agent.CaptureDebugSnapshot());
            }

            if (GUILayout.Button(
                    new GUIContent("Open Graph", "Open and frame this Agent or Snapshot in Planner Graph."),
                    GUILayout.Width(80f)))
            {
                GoapEditorWindow.OpenForRuntime(_agent, _selectedSnapshot, true);
            }

            using (new EditorGUI.DisabledScope(!ViewingSnapshot))
            {
                if (GUILayout.Button("Restore & Replan", GUILayout.Width(112f)))
                {
                    RestoreSelectedSnapshot();
                }
            }

            if (GUILayout.Button("Copy", GUILayout.Width(48f)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildSnapshotText();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverview()
        {
            DrawHeader("Configuration");
            EditorGUILayout.LabelField("Domain", ViewedDomain != null ? ViewedDomain.name : "Missing");
            EditorGUILayout.LabelField("Profile", _agent.Profile != null ? _agent.Profile.name : "Direct domain configuration");
            EditorGUILayout.LabelField("Source", ViewingSnapshot ? $"Snapshot #{_selectedSnapshot.Sequence}" : "Live");

            DrawPlan();

            DrawHeader("Decision Summary");
            var goals = ViewedGoalDiagnostics;
            var actions = ViewedActionDiagnostics;
            EditorGUILayout.LabelField(
                "Goals",
                $"{goals.Count(item => item.Eligible)} eligible, " +
                $"{goals.Count(item => item.OnCooldown)} cooldown, " +
                $"{goals.Count(item => item.Satisfied)} satisfied, {goals.Count(item => !item.Active)} inactive");
            EditorGUILayout.LabelField(
                "Actions",
                $"{actions.Count(item => item.Executable && item.ExecutorDiagnostic.Status == GoapExecutorDiagnosticStatus.Ready)} ready, " +
                $"{actions.Count(item => item.PreconditionsSatisfied && item.ExecutorDiagnostic.Status == GoapExecutorDiagnosticStatus.Warning)} warnings, " +
                $"{actions.Count(item => item.HasExecutor && !item.PreconditionsSatisfied)} waiting, " +
                $"{actions.Count(item => item.HasExecutor && item.PreconditionsSatisfied && !item.ExecutorDiagnostic.CanStart)} blocked, " +
                $"{actions.Count(item => !item.HasExecutor)} without executor");

            var blocked = actions.Where(item => !item.Executable).Take(4).ToArray();
            if (blocked.Length > 0)
            {
                DrawHeader("Top Action Blockers");
                foreach (var item in blocked)
                {
                    EditorGUILayout.LabelField(item.Action.DisplayName, item.Reason);
                }
            }

            DrawHeader("Recent Events");
            DrawTrace(8);
        }

        private void DrawWorldState()
        {
            DrawHeader("World State");
            if (ViewedDomain == null || (!ViewingSnapshot && _agent.WorldState == null))
            {
                EditorGUILayout.LabelField("Not initialized");
                return;
            }

            _factSearch = EditorGUILayout.TextField("Search", _factSearch);
            foreach (var fact in ViewedDomain.Facts.Where(fact => fact != null))
            {
                if (!MatchesSearch(fact.DisplayName, _factSearch))
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(fact.DisplayName, GUILayout.MinWidth(190f));
                EditorGUILayout.LabelField(fact.ValueType.ToString(), GUILayout.Width(65f));
                EditorGUILayout.SelectableLabel(
                    fact.FormatValue(GetViewedValue(fact)),
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPlan()
        {
            DrawHeader("Current Plan");
            var planSummary = ViewingSnapshot ? _selectedSnapshot.PlanSummary : _agent.GetPlanSummary();
            EditorGUILayout.LabelField(planSummary, EditorStyles.wordWrappedLabel);

            var hasPlan = ViewingSnapshot ? _selectedSnapshot.HasPlan : _agent.LastPlan != null;
            if (hasPlan)
            {
                var cost = ViewingSnapshot ? _selectedSnapshot.PlanCost : _agent.LastPlan.TotalCost;
                var expanded = ViewingSnapshot ? _selectedSnapshot.ExpandedStates : _agent.LastPlan.ExpandedStates;
                var milliseconds = ViewingSnapshot
                    ? _selectedSnapshot.PlanningMilliseconds
                    : _agent.LastPlan.PlanningMilliseconds;
                EditorGUILayout.LabelField(
                    "Metrics",
                    $"Cost {cost:0.##} | Expanded {expanded} | {milliseconds:0.###} ms");
            }

            var failure = ViewingSnapshot ? _selectedSnapshot.PlanningFailure : _agent.LastPlanningFailure;
            var message = ViewingSnapshot ? _selectedSnapshot.PlanningMessage : _agent.LastPlanningMessage;
            if (failure != GoapPlanFailure.None)
            {
                EditorGUILayout.HelpBox($"{failure}: {message}", MessageType.Warning);
            }
        }

        private void DrawGoals()
        {
            DrawHeader("Goal Evaluation");
            foreach (var item in ViewedGoalDiagnostics
                         .OrderByDescending(item => item.Eligible)
                         .ThenByDescending(item => item.FinalScore))
            {
                var key = item.Goal.GetInstanceID();
                _goalFoldouts.TryGetValue(key, out var expanded);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var selected = item.Goal == ViewedGoal
                    ? "SELECTED"
                    : item.Satisfied
                        ? "DONE"
                        : item.OnCooldown
                            ? "COOLDOWN"
                            : item.Active ? "ELIGIBLE" : "INACTIVE";
                expanded = EditorGUILayout.Foldout(
                    expanded,
                    $"[{selected}] {item.Goal.DisplayName}",
                    true,
                    EditorStyles.foldoutHeader);
                EditorGUILayout.LabelField($"Score {item.FinalScore:0.##}", GUILayout.Width(82f));
                EditorGUILayout.EndHorizontal();
                _goalFoldouts[key] = expanded;
                EditorGUILayout.LabelField(item.Reason, EditorStyles.wordWrappedMiniLabel);

                if (expanded)
                {
                    EditorGUILayout.LabelField("Score", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Base Priority", item.BaseScore.ToString("0.##"));
                    foreach (var term in item.ScoreTerms)
                    {
                        EditorGUILayout.LabelField(term.Label, term.Value.ToString("+0.##;-0.##;0"));
                    }

                    if (item.OnCooldown)
                    {
                        EditorGUILayout.LabelField("Cooldown", $"{item.CooldownRemaining:0.0}s");
                    }

                    DrawConditionGroup("Activation", item.ActivationConditions);
                    DrawConditionGroup("Desired State", item.DesiredState);
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawActions()
        {
            DrawHeader("Action Evaluation");
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _actionSearch = GUILayout.TextField(
                _actionSearch,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(150f));
            _blockedActionsOnly = GUILayout.Toggle(
                _blockedActionsOnly,
                "Blocked Only",
                EditorStyles.toolbarButton,
                GUILayout.Width(88f));
            EditorGUILayout.EndHorizontal();

            var diagnostics = ViewedActionDiagnostics
                .Where(item => item.Action != null)
                .Where(item => MatchesSearch(item.Action.DisplayName, _actionSearch))
                .Where(item => !_blockedActionsOnly || !item.Executable)
                .OrderBy(item => item.Executable ? 0 : item.HasExecutor ? 1 : 2)
                .ThenBy(item => item.Action.DisplayName);
            foreach (var item in diagnostics)
            {
                var key = item.Action.GetInstanceID();
                _actionFoldouts.TryGetValue(key, out var expanded);
                var state = item.Action == ViewedAction
                    ? "RUNNING"
                    : !item.HasExecutor
                        ? "NO EXECUTOR"
                        : !item.PreconditionsSatisfied
                            ? "WAITING"
                            : item.ExecutorDiagnostic.Status == GoapExecutorDiagnosticStatus.Blocked
                                ? "BLOCKED"
                                : item.ExecutorDiagnostic.Status == GoapExecutorDiagnosticStatus.Warning
                                    ? "WARNING"
                                    : "READY";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                expanded = EditorGUILayout.Foldout(
                    expanded,
                    $"[{state}] {item.Action.DisplayName}",
                    true,
                    EditorStyles.foldoutHeader);
                var costLabel = item.PlanningAvailable ? item.PlanningCost.ToString("0.##") : "n/a";
                EditorGUILayout.LabelField($"Cost {costLabel}", GUILayout.Width(70f));
                EditorGUILayout.EndHorizontal();
                _actionFoldouts[key] = expanded;
                EditorGUILayout.LabelField(item.Reason, EditorStyles.wordWrappedMiniLabel);

                if (expanded)
                {
                    var executor = item.Action.UsesBuiltInExecutor
                        ? "Built-in executor"
                        : string.IsNullOrWhiteSpace(item.Action.ExecutorId)
                            ? "Not configured"
                            : item.Action.ExecutorId;
                    EditorGUILayout.LabelField(
                        "Executor",
                        item.HasExecutor ? executor : $"Missing: {executor}");
                    EditorGUILayout.LabelField("Base Cost", item.BaseCost.ToString("0.##"));
                    EditorGUILayout.LabelField(
                        "Context Cost",
                        item.PlanningAvailable
                            ? item.PlanningCost.ToString("0.##")
                            : "Unavailable target/context");
                    if (item.HasExecutor)
                    {
                        EditorGUILayout.LabelField(
                            "Preflight",
                            $"{item.ExecutorDiagnostic.Status} | {item.ExecutorDiagnostic.Code}");
                    }
                    DrawConditionGroup("Preconditions", item.Preconditions);
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawHistory()
        {
            DrawHeader("Decision Snapshots");
            EditorGUILayout.HelpBox(
                "Snapshots are captured on goal, plan and action events. Selecting one only changes this view. " +
                "Restore & Replan replaces the live World State; scene objects and inventories are not rolled back.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_agent.DecisionSnapshots.Count == 0))
            {
                if (GUILayout.Button("Latest", GUILayout.Width(70f)))
                {
                    SelectSnapshot(_agent.DecisionSnapshots[^1]);
                }
            }

            if (GUILayout.Button("Live", GUILayout.Width(56f)))
            {
                ReturnToLive();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear History", GUILayout.Width(92f)))
            {
                _agent.ClearDebugHistory();
                ReturnToLive();
            }

            EditorGUILayout.EndHorizontal();

            for (var index = _agent.DecisionSnapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = _agent.DecisionSnapshots[index];
                var previousColor = GUI.backgroundColor;
                if (snapshot == _selectedSnapshot)
                {
                    GUI.backgroundColor = new Color(0.45f, 0.72f, 1f);
                }

                if (GUILayout.Button(
                        $"#{snapshot.Sequence}  {snapshot.Time,7:0.00}s  {snapshot.Trigger}  |  {snapshot.Reason}",
                        EditorStyles.miniButton,
                        GUILayout.Height(24f)))
                {
                    SelectSnapshot(snapshot);
                }

                GUI.backgroundColor = previousColor;
            }

            if (ViewingSnapshot)
            {
                DrawHeader($"Snapshot #{_selectedSnapshot.Sequence}");
                EditorGUILayout.LabelField("Trigger", _selectedSnapshot.Trigger.ToString());
                EditorGUILayout.LabelField("Reason", _selectedSnapshot.Reason, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Status", _selectedSnapshot.Status, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Plan", _selectedSnapshot.PlanSummary, EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Restore & Replan", GUILayout.Width(112f)))
                {
                    RestoreSelectedSnapshot();
                }

                if (GUILayout.Button("Copy Snapshot", GUILayout.Width(104f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildSnapshotText();
                }

                EditorGUILayout.EndHorizontal();
            }

            _showEventLog = EditorGUILayout.Foldout(_showEventLog, "Raw Event Log", true);
            if (_showEventLog)
            {
                DrawTrace(int.MaxValue);
            }
        }

        private static void DrawConditionGroup(
            string title,
            IReadOnlyList<GoapConditionDiagnostic> conditions)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (conditions.Count == 0)
            {
                EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                return;
            }

            foreach (var condition in conditions)
            {
                var marker = condition.Satisfied ? "OK" : "BLOCKED";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(marker, GUILayout.Width(58f));
                EditorGUILayout.LabelField(condition.Requirement, GUILayout.MinWidth(180f));
                EditorGUILayout.LabelField($"Actual: {condition.Actual}", GUILayout.MinWidth(110f));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTrace(int maxEntries)
        {
            var first = Mathf.Max(0, _agent.Trace.Count - maxEntries);
            for (var index = _agent.Trace.Count - 1; index >= first; index--)
            {
                var item = _agent.Trace[index];
                EditorGUILayout.LabelField(
                    new GUIContent($"{item.Time,7:0.00}  {item.Type}"),
                    new GUIContent(item.Message),
                    EditorStyles.wordWrappedLabel);
            }
        }

        private string BuildSnapshotText()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Agent: {_agent.name}");
            builder.AppendLine($"Source: {(ViewingSnapshot ? $"Snapshot #{_selectedSnapshot.Sequence}" : "Live")}");
            if (ViewingSnapshot)
            {
                builder.AppendLine($"Trigger: {_selectedSnapshot.Trigger} - {_selectedSnapshot.Reason}");
            }

            builder.AppendLine($"Status: {(ViewingSnapshot ? _selectedSnapshot.Status : _agent.StatusMessage)}");
            builder.AppendLine($"Goal: {ViewedGoal?.DisplayName ?? "None"}");
            builder.AppendLine($"Action: {ViewedAction?.DisplayName ?? "None"}");
            builder.AppendLine($"Plan: {(ViewingSnapshot ? _selectedSnapshot.PlanSummary : _agent.GetPlanSummary())}");

            if (ViewedDomain != null)
            {
                builder.AppendLine("Facts:");
                foreach (var fact in ViewedDomain.Facts.Where(fact => fact != null))
                {
                    builder.AppendLine($"  {fact.DisplayName} = {fact.FormatValue(GetViewedValue(fact))}");
                }
            }

            var blocked = ViewedActionDiagnostics.Where(item => !item.Executable).ToArray();
            if (blocked.Length > 0)
            {
                builder.AppendLine("Blocked Actions:");
                foreach (var item in blocked)
                {
                    builder.AppendLine($"  {item.Action.DisplayName}: {item.Reason}");
                }
            }

            return builder.ToString();
        }

        private GoapValue GetViewedValue(GoapFact fact)
        {
            return ViewingSnapshot ? _selectedSnapshot.GetValue(fact) : _agent.WorldState.GetValue(fact);
        }

        private void RestoreSelectedSnapshot()
        {
            if (!ViewingSnapshot || !_agent.RestoreDebugSnapshot(_selectedSnapshot))
            {
                return;
            }

            _agent.ForceDecision();
            if (_agent.Paused)
            {
                _agent.StepDecision();
            }

            _selectedSnapshot = null;
            GoapRuntimeDebugContext.Set(_agent, null);
        }

        private void SelectSnapshot(GoapDecisionSnapshot snapshot)
        {
            _selectedSnapshot = snapshot;
            if (snapshot != null)
            {
                _selectedTab = 4;
            }

            GoapRuntimeDebugContext.Set(_agent, snapshot);
        }

        private void ValidateSelectedSnapshot()
        {
            if (_selectedSnapshot != null && !_agent.DecisionSnapshots.Contains(_selectedSnapshot))
            {
                ReturnToLive();
            }
        }

        private void FollowSelection()
        {
            if (!_followSelection || Selection.activeGameObject == null)
            {
                return;
            }

            var selectedAgent = Selection.activeGameObject.GetComponentInParent<GoapAgent>();
            if (selectedAgent != null)
            {
                SetAgent(selectedAgent);
                Repaint();
            }
        }

        private void SetAgent(GoapAgent agent)
        {
            if (_agent == agent)
            {
                return;
            }

            _agent = agent;
            _selectedSnapshot = null;
            _actionFoldouts.Clear();
            _goalFoldouts.Clear();
            GoapRuntimeDebugContext.Set(agent, null);
        }

        private void ReturnToLive()
        {
            _selectedSnapshot = null;
            GoapRuntimeDebugContext.Set(_agent, null);
        }

        private void RepaintOnInterval()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint)
            {
                return;
            }

            _nextRepaint = EditorApplication.timeSinceStartup + 0.2d;
            Repaint();
        }

        private static bool MatchesSearch(string value, string search)
        {
            return string.IsNullOrWhiteSpace(search) ||
                   value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawHeader(string title)
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }

    internal static class GoapRuntimeDebugContext
    {
        public static GoapAgent Agent { get; private set; }
        public static GoapDecisionSnapshot Snapshot { get; private set; }
        public static event Action Changed;

        public static void Set(
            GoapAgent agent,
            GoapDecisionSnapshot snapshot)
        {
            if (agent == null)
            {
                snapshot = null;
            }

            var changed = Agent != agent || Snapshot != snapshot;
            Agent = agent;
            Snapshot = snapshot;
            if (changed)
            {
                Changed?.Invoke();
            }
        }
    }
}
