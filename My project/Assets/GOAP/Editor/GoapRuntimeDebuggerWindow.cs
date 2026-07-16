using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    public sealed class GoapRuntimeDebuggerWindow : EditorWindow
    {
        private GoapAgent _agent;
        private Vector2 _scroll;
        private bool _followSelection = true;
        private double _nextRepaint;
        private GoapWorldState _snapshot;
        private string _snapshotAgentName;

        [MenuItem("Tools/GOAP/Runtime Debugger %#d")]
        public static void Open()
        {
            var window = GetWindow<GoapRuntimeDebuggerWindow>();
            window.titleContent = new GUIContent("GOAP Debugger");
            window.minSize = new Vector2(520f, 520f);
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

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSummary();
            DrawWorldState();
            DrawPlan();
            DrawGoals();
            DrawActions();
            DrawTrace();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _agent = (GoapAgent)EditorGUILayout.ObjectField(
                _agent,
                typeof(GoapAgent),
                true,
                GUILayout.MinWidth(180f));
            _followSelection = GUILayout.Toggle(_followSelection, "Follow Selection", EditorStyles.toolbarButton);
            if (GUILayout.Button("Find", EditorStyles.toolbarButton, GUILayout.Width(46f)))
            {
                _agent = FindFirstObjectByType<GoapAgent>();
                if (_agent != null)
                {
                    Selection.activeGameObject = _agent.gameObject;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAgentPicker()
        {
            foreach (var agent in FindObjectsByType<GoapAgent>(FindObjectsSortMode.None))
            {
                if (GUILayout.Button(agent.name))
                {
                    _agent = agent;
                    Selection.activeGameObject = agent.gameObject;
                }
            }
        }

        private void DrawSummary()
        {
            DrawHeader("Agent");
            EditorGUILayout.LabelField("Name", _agent.name);
            EditorGUILayout.LabelField("Domain", _agent.Domain != null ? _agent.Domain.name : "Missing");
            EditorGUILayout.LabelField("Profile", _agent.Profile != null ? _agent.Profile.name : "Direct domain configuration");
            EditorGUILayout.LabelField("Status", _agent.StatusMessage);
            EditorGUILayout.LabelField("Goal", _agent.CurrentGoal != null ? _agent.CurrentGoal.DisplayName : "None");
            EditorGUILayout.LabelField("Action", _agent.CurrentAction != null ? _agent.CurrentAction.DisplayName : "None");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_agent.Paused ? "Resume" : "Pause"))
            {
                _agent.SetPaused(!_agent.Paused);
            }

            if (GUILayout.Button("Step Action"))
            {
                _agent.StepAction();
            }

            if (GUILayout.Button("Replan"))
            {
                _agent.ForceDecision();
                _agent.StepDecision();
            }

            if (GUILayout.Button("Abort Action"))
            {
                _agent.AbortCurrentAction();
            }

            if (GUILayout.Button("Capture"))
            {
                _snapshot = _agent.CaptureWorldState();
                _snapshotAgentName = _agent.name;
            }

            using (new EditorGUI.DisabledScope(_snapshot == null))
            {
                if (GUILayout.Button("Restore"))
                {
                    _agent.RestoreWorldState(_snapshot);
                }
            }

            if (GUILayout.Button("Copy"))
            {
                EditorGUIUtility.systemCopyBuffer = BuildSnapshot();
            }

            EditorGUILayout.EndHorizontal();
            if (_snapshot != null)
            {
                EditorGUILayout.LabelField("Snapshot", $"Captured from {_snapshotAgentName}");
            }
        }

        private void DrawWorldState()
        {
            DrawHeader("World State");
            if (_agent.WorldState == null || _agent.Domain == null)
            {
                EditorGUILayout.LabelField("Not initialized");
                return;
            }

            foreach (var fact in _agent.Domain.Facts.Where(fact => fact != null))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(fact.DisplayName, GUILayout.MinWidth(190f));
                EditorGUILayout.LabelField(fact.ValueType.ToString(), GUILayout.Width(65f));
                EditorGUILayout.SelectableLabel(
                    fact.FormatValue(_agent.WorldState.GetValue(fact)),
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPlan()
        {
            DrawHeader("Current Plan");
            EditorGUILayout.LabelField(_agent.GetPlanSummary(), EditorStyles.wordWrappedLabel);
            if (_agent.LastPlan != null)
            {
                EditorGUILayout.LabelField(
                    "Metrics",
                    $"Cost {_agent.LastPlan.TotalCost:0.##} | " +
                    $"Expanded {_agent.LastPlan.ExpandedStates} | " +
                    $"{_agent.LastPlan.PlanningMilliseconds:0.###} ms");
            }
        }

        private void DrawGoals()
        {
            DrawHeader("Goal Evaluation");
            foreach (var item in _agent.GoalDiagnostics)
            {
                var marker = item.Goal == _agent.CurrentGoal ? ">" : " ";
                EditorGUILayout.LabelField(
                    $"{marker} [{item.Goal.Priority}] {item.Goal.DisplayName}",
                    item.Reason);
            }
        }

        private void DrawActions()
        {
            DrawHeader("Action Evaluation");
            foreach (var item in _agent.ActionDiagnostics)
            {
                var marker = item.Action == _agent.CurrentAction ? ">" : item.Executable ? "+" : "!";
                EditorGUILayout.LabelField($"{marker} {item.Action.DisplayName}", item.Reason);
            }
        }

        private void DrawTrace()
        {
            DrawHeader("Decision History");
            for (var index = _agent.Trace.Count - 1; index >= 0; index--)
            {
                var item = _agent.Trace[index];
                EditorGUILayout.LabelField(
                    new GUIContent($"{item.Time,7:0.00}  {item.Type}"),
                    new GUIContent(item.Message),
                    EditorStyles.wordWrappedLabel);
            }
        }

        private string BuildSnapshot()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Agent: {_agent.name}");
            builder.AppendLine($"Status: {_agent.StatusMessage}");
            builder.AppendLine($"Goal: {_agent.CurrentGoal?.DisplayName ?? "None"}");
            builder.AppendLine($"Plan: {_agent.GetPlanSummary()}");
            if (_agent.Domain != null && _agent.WorldState != null)
            {
                builder.AppendLine("Facts:");
                foreach (var fact in _agent.Domain.Facts.Where(fact => fact != null))
                {
                    builder.AppendLine($"  {fact.DisplayName} = {fact.FormatValue(_agent.WorldState.GetValue(fact))}");
                }
            }

            return builder.ToString();
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
                _agent = selectedAgent;
                Repaint();
            }
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

        private static void DrawHeader(string title)
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
