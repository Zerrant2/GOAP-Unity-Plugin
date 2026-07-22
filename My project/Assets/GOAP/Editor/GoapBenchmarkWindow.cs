using Practice.GOAP.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Practice.GOAP.Editor
{
    public sealed class GoapBenchmarkWindow : EditorWindow
    {
        private const double RepaintInterval = 0.2d;
        private static readonly int[] AgentCounts = { 10, 100, 500 };

        private bool _budgetEnabled = true;
        private int _maxPlansPerFrame = GoapPlanningScheduler.DefaultMaxPlansPerFrame;
        private float _maxPlanningMilliseconds =
            (float)GoapPlanningScheduler.DefaultMaxPlanningMillisecondsPerFrame;
        private GoapBenchmarkMode _mode = GoapBenchmarkMode.Visual;
        private int _modeRunnerId;
        private double _nextRepaintTime;

        [MenuItem("Tools/GOAP/Benchmark Dashboard")]
        public static void Open()
        {
            var window = GetWindow<GoapBenchmarkWindow>();
            window.titleContent = new GUIContent("GOAP Benchmark");
            window.minSize = new Vector2(440f, 430f);
            window.Show();
        }

        private void OnEnable()
        {
            _budgetEnabled = GoapPlanningScheduler.Enabled;
            _maxPlansPerFrame = GoapPlanningScheduler.MaxPlansPerFrame;
            _maxPlanningMilliseconds = (float)GoapPlanningScheduler.MaxPlanningMillisecondsPerFrame;
            EditorApplication.update += RepaintWhileRunning;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhileRunning;
        }

        private void OnGUI()
        {
            var runner = FindFirstObjectByType<GoapBenchmarkRunner>();
            if (runner != null && runner.GetInstanceID() != _modeRunnerId)
            {
                _mode = runner.Mode;
                _modeRunnerId = runner.GetInstanceID();
            }

            EditorGUILayout.LabelField("Benchmark Scenes", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var count in AgentCounts)
                {
                    if (GUILayout.Button($"Open {count} NPC", GUILayout.Height(26f)))
                    {
                        OpenBenchmarkScene(count);
                    }
                }

                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Build / Refresh Benchmark Scenes"))
                {
                    GoapDemoProjectBuilder.BuildBenchmarkScenes();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Benchmark Mode", EditorStyles.boldLabel);
            _mode = (GoapBenchmarkMode)EditorGUILayout.EnumPopup("Mode", _mode);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Planning Budget", EditorStyles.boldLabel);
            _budgetEnabled = EditorGUILayout.Toggle("Enabled", _budgetEnabled);
            _maxPlansPerFrame = Mathf.Max(1, EditorGUILayout.IntField("Max plans / frame", _maxPlansPerFrame));
            _maxPlanningMilliseconds = Mathf.Max(
                0.1f,
                EditorGUILayout.FloatField("Max planning ms / frame", _maxPlanningMilliseconds));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Settings"))
            {
                if (runner != null)
                {
                    if (!Application.isPlaying)
                    {
                        Undo.RecordObject(runner, "Change GOAP benchmark settings");
                    }

                    runner.SetMode(_mode);
                    runner.ConfigurePlanningBudget(
                        _budgetEnabled,
                        _maxPlansPerFrame,
                        _maxPlanningMilliseconds);
                    if (!Application.isPlaying)
                    {
                        EditorUtility.SetDirty(runner);
                    }
                }
                else
                {
                    GoapPlanningScheduler.Configure(
                        _budgetEnabled,
                        _maxPlansPerFrame,
                        _maxPlanningMilliseconds);
                }
            }

            if (GUILayout.Button(EditorApplication.isPlaying ? "Stop" : "Play"))
            {
                EditorApplication.isPlaying = !EditorApplication.isPlaying;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Live Metrics", EditorStyles.boldLabel);
            if (runner == null)
            {
                EditorGUILayout.HelpBox(
                    "Open a GOAP Benchmark scene to inspect its metrics.",
                    MessageType.Info);
                DrawSchedulerMetrics(GoapPlanningScheduler.Metrics);
                return;
            }

            DrawRunnerMetrics(runner);
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Reset Metrics"))
                {
                    runner.ResetMetrics();
                }
            }

            if (GUILayout.Button("Copy Summary"))
            {
                EditorGUIUtility.systemCopyBuffer = runner.BuildSummary();
                ShowNotification(new GUIContent("Benchmark summary copied"));
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawRunnerMetrics(GoapBenchmarkRunner runner)
        {
            var total = Mathf.Max(1, runner.Agents.Count);
            var progress = Mathf.Clamp01((float)runner.PlannedAgents / total);
            var rect = GUILayoutUtility.GetRect(18f, 18f);
            EditorGUI.ProgressBar(
                rect,
                progress,
                $"Planned agents: {runner.PlannedAgents}/{runner.Agents.Count}");
            EditorGUILayout.LabelField("Observed plans", runner.ObservedPlanCount.ToString());
            EditorGUILayout.LabelField(
                "Search avg / median / p95",
                $"{runner.AveragePlanningMilliseconds:0.###} / " +
                $"{runner.MedianPlanningMilliseconds:0.###} / " +
                $"{runner.P95PlanningMilliseconds:0.###} ms");
            EditorGUILayout.LabelField("Search maximum", $"{runner.MaximumPlanningMilliseconds:0.###} ms");
            EditorGUILayout.LabelField(
                "Expanded states avg / max",
                $"{runner.AverageExpandedStates:0.#} / {runner.MaximumExpandedStates}");
            EditorGUILayout.LabelField(
                "Frame time / FPS",
                $"{runner.AverageFrameMilliseconds:0.##} ms / {runner.FramesPerSecond:0.#}");
            DrawSchedulerMetrics(runner.SchedulerMetrics);
        }

        private static void DrawSchedulerMetrics(GoapPlanningSchedulerMetrics metrics)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Scheduler this frame",
                $"{metrics.GrantedThisFrame} granted / {metrics.DeferredThisFrame} deferred");
            EditorGUILayout.LabelField("Waiting agents", metrics.QueuedRequests.ToString());
            EditorGUILayout.LabelField(
                "Scheduler total",
                $"{metrics.CompletedPlans} completed / {metrics.TotalDeferred} deferred");
            EditorGUILayout.LabelField(
                "Planning avg / peak frame",
                $"{metrics.AveragePlanningMilliseconds:0.###} / " +
                $"{metrics.PeakPlanningMillisecondsPerFrame:0.###} ms");
            EditorGUILayout.LabelField("Peak plans / frame", metrics.PeakPlansPerFrame.ToString());
        }

        private static void OpenBenchmarkScene(int agentCount)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var path = $"{GoapDemoProjectBuilder.BenchmarkFolder}/GOAP Benchmark {agentCount}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                GoapDemoProjectBuilder.BuildBenchmarkScenes();
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private void RepaintWhileRunning()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextRepaintTime)
            {
                return;
            }

            _nextRepaintTime = EditorApplication.timeSinceStartup + RepaintInterval;
            Repaint();
        }
    }
}
