using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Practice.GOAP.Demo
{
    public enum GoapBenchmarkMode
    {
        LogicOnly,
        Visual
    }

    public sealed class GoapBenchmarkRunner : MonoBehaviour
    {
        private const int MaxFrameSamples = 240;
        private const int MaxPlanSamples = 10000;
        private static readonly ProfilerMarker SpawnMarker = new("GOAP.Benchmark.SpawnAgents");

        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField, Min(1)] private int _agentCount = 100;
        [SerializeField, Min(1f)] private float _spacing = 1.25f;
        [Header("Visualization")]
        [SerializeField] private GoapBenchmarkMode _mode = GoapBenchmarkMode.Visual;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _queuedMaterial;
        [SerializeField] private Material _activeMaterial;
        [SerializeField] private Material _completedMaterial;
        [SerializeField] private Renderer[] _environmentRenderers = Array.Empty<Renderer>();
        [Header("Planning Budget")]
        [SerializeField] private bool _enablePlanningBudget = true;
        [SerializeField, Min(1)] private int _maxPlansPerFrame = GoapPlanningScheduler.DefaultMaxPlansPerFrame;
        [SerializeField, Min(0.1f)] private float _maxPlanningMillisecondsPerFrame =
            (float)GoapPlanningScheduler.DefaultMaxPlanningMillisecondsPerFrame;

        private readonly List<GoapAgent> _agents = new();
        private readonly List<Renderer> _agentRenderers = new();
        private readonly List<Vector3> _displayPositions = new();
        private readonly Dictionary<GoapAgent, GoapPlan> _observedPlans = new();
        private readonly List<double> _planningSamples = new();
        private readonly List<int> _expandedStateSamples = new();
        private readonly Queue<float> _frameSamples = new();
        private float _nextMetricsTime;
        private bool _schedulerConfigured;
        private Mesh _agentMesh;

        public IReadOnlyList<GoapAgent> Agents => _agents;
        public GoapBenchmarkMode Mode => _mode;
        public int ConfiguredAgentCount => _agentCount;
        public int PlannedAgents => _observedPlans.Count;
        public int ObservedPlanCount => _planningSamples.Count;
        public int QueuedAgents { get; private set; }
        public int ActiveAgents { get; private set; }
        public int CompletedAgents { get; private set; }
        public double AveragePlanningMilliseconds { get; private set; }
        public double MedianPlanningMilliseconds { get; private set; }
        public double P95PlanningMilliseconds { get; private set; }
        public double MaximumPlanningMilliseconds { get; private set; }
        public double AverageExpandedStates { get; private set; }
        public int MaximumExpandedStates { get; private set; }
        public double AverageFrameMilliseconds { get; private set; }
        public double FramesPerSecond => AverageFrameMilliseconds <= 0d ? 0d : 1000d / AverageFrameMilliseconds;
        public GoapPlanningSchedulerMetrics SchedulerMetrics => GoapPlanningScheduler.Metrics;

        public void Configure(GoapAgentProfile profile, int agentCount)
        {
            _profile = profile;
            _agentCount = Mathf.Max(1, agentCount);
        }

        public void ConfigureVisualization(
            Material idleMaterial,
            Material queuedMaterial,
            Material activeMaterial,
            Material completedMaterial,
            params Renderer[] environmentRenderers)
        {
            _idleMaterial = idleMaterial;
            _queuedMaterial = queuedMaterial;
            _activeMaterial = activeMaterial;
            _completedMaterial = completedMaterial;
            _environmentRenderers = environmentRenderers ?? Array.Empty<Renderer>();
        }

        public void SetMode(GoapBenchmarkMode mode)
        {
            _mode = mode;
            ApplyVisualizationMode();
        }

        public void ConfigurePlanningBudget(bool enabled, int maxPlansPerFrame, float maxMillisecondsPerFrame)
        {
            _enablePlanningBudget = enabled;
            _maxPlansPerFrame = Mathf.Max(1, maxPlansPerFrame);
            _maxPlanningMillisecondsPerFrame = Mathf.Max(0.1f, maxMillisecondsPerFrame);
            if (Application.isPlaying)
            {
                ApplyPlanningBudget();
            }
        }

        public void ResetMetrics()
        {
            _observedPlans.Clear();
            _planningSamples.Clear();
            _expandedStateSamples.Clear();
            _frameSamples.Clear();
            AveragePlanningMilliseconds = 0d;
            MedianPlanningMilliseconds = 0d;
            P95PlanningMilliseconds = 0d;
            MaximumPlanningMilliseconds = 0d;
            AverageExpandedStates = 0d;
            MaximumExpandedStates = 0;
            AverageFrameMilliseconds = 0d;
            GoapPlanningScheduler.ResetMetrics();
        }

        public string BuildSummary()
        {
            var scheduler = SchedulerMetrics;
            var summary = new StringBuilder();
            summary.AppendLine($"GOAP Benchmark: {_agentCount} agents ({_mode})");
            summary.AppendLine($"Planned agents: {PlannedAgents}/{_agents.Count}");
            summary.AppendLine(
                $"Agent states: {QueuedAgents} queued, {ActiveAgents} active, {CompletedAgents} completed");
            summary.AppendLine($"Observed plans: {ObservedPlanCount}");
            summary.AppendLine(
                $"Search ms: avg {AveragePlanningMilliseconds:0.###}, median {MedianPlanningMilliseconds:0.###}, " +
                $"p95 {P95PlanningMilliseconds:0.###}, max {MaximumPlanningMilliseconds:0.###}");
            summary.AppendLine(
                $"Expanded states: avg {AverageExpandedStates:0.#}, max {MaximumExpandedStates}");
            summary.AppendLine(
                $"Frame: {AverageFrameMilliseconds:0.##} ms ({FramesPerSecond:0.#} FPS)");
            summary.AppendLine(
                $"Scheduler: {scheduler.CompletedPlans} completed, {scheduler.TotalDeferred} deferred, " +
                $"{scheduler.QueuedRequests} queued, peak {scheduler.PeakPlanningMillisecondsPerFrame:0.###} ms/frame");
            return summary.ToString().TrimEnd();
        }

        private void Start()
        {
            ApplyPlanningBudget();
            using var marker = SpawnMarker.Auto();
            var columns = Mathf.CeilToInt(Mathf.Sqrt(_agentCount));
            var rows = Mathf.CeilToInt((float)_agentCount / columns);
            var horizontalOffset = (columns - 1) * _spacing * 0.5f;
            var verticalOffset = (rows - 1) * _spacing * 0.5f;
            _agentMesh = GetCapsuleMesh();
            for (var index = 0; index < _agentCount; index++)
            {
                var agentObject = new GameObject($"Benchmark Agent {index + 1}");
                agentObject.transform.SetParent(transform, false);
                var displayPosition = new Vector3(
                    (index % columns) * _spacing - horizontalOffset,
                    0.55f,
                    (index / columns) * _spacing - verticalOffset);
                agentObject.transform.localPosition = displayPosition;
                agentObject.transform.localScale = new Vector3(0.42f, 0.55f, 0.42f);
                var meshFilter = agentObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = _agentMesh;
                var meshRenderer = agentObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _idleMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                agentObject.AddComponent<GoapInventory>();
                var authoring = agentObject.AddComponent<GoapAgentAuthoring>();
                authoring.Configure(_profile);
                _agents.Add(agentObject.GetComponent<GoapAgent>());
                _agentRenderers.Add(meshRenderer);
                _displayPositions.Add(displayPosition);
            }

            RefreshVisuals();
            ApplyVisualizationMode();
        }

        private void OnDestroy()
        {
            if (_schedulerConfigured)
            {
                GoapPlanningScheduler.RestoreDefaults();
            }
        }

        private void Update()
        {
            ObservePlans();
            ObserveFrameTime();
            if (Time.unscaledTime < _nextMetricsTime)
            {
                return;
            }

            _nextMetricsTime = Time.unscaledTime + 0.25f;
            RefreshMetrics();
            RefreshVisuals();
        }

        private void ApplyPlanningBudget()
        {
            GoapPlanningScheduler.Configure(
                _enablePlanningBudget,
                _maxPlansPerFrame,
                _maxPlanningMillisecondsPerFrame);
            _schedulerConfigured = true;
        }

        private void ObservePlans()
        {
            foreach (var agent in _agents)
            {
                if (agent == null || agent.LastPlan == null ||
                    (_observedPlans.TryGetValue(agent, out var observed) && ReferenceEquals(observed, agent.LastPlan)))
                {
                    continue;
                }

                _observedPlans[agent] = agent.LastPlan;
                if (_planningSamples.Count >= MaxPlanSamples)
                {
                    _planningSamples.RemoveAt(0);
                    _expandedStateSamples.RemoveAt(0);
                }

                _planningSamples.Add(agent.LastPlan.PlanningMilliseconds);
                _expandedStateSamples.Add(agent.LastPlan.ExpandedStates);
            }
        }

        private void ObserveFrameTime()
        {
            if (Time.unscaledDeltaTime <= 0f)
            {
                return;
            }

            _frameSamples.Enqueue(Time.unscaledDeltaTime * 1000f);
            while (_frameSamples.Count > MaxFrameSamples)
            {
                _frameSamples.Dequeue();
            }
        }

        private void RefreshMetrics()
        {
            if (_planningSamples.Count > 0)
            {
                var sorted = _planningSamples.OrderBy(value => value).ToArray();
                AveragePlanningMilliseconds = _planningSamples.Average();
                MedianPlanningMilliseconds = Percentile(sorted, 0.5d);
                P95PlanningMilliseconds = Percentile(sorted, 0.95d);
                MaximumPlanningMilliseconds = sorted[^1];
                AverageExpandedStates = _expandedStateSamples.Average();
                MaximumExpandedStates = _expandedStateSamples.Max();
            }

            AverageFrameMilliseconds = _frameSamples.Count == 0 ? 0d : _frameSamples.Average();
        }

        private void RefreshVisuals()
        {
            QueuedAgents = 0;
            ActiveAgents = 0;
            CompletedAgents = 0;
            for (var index = 0; index < _agents.Count; index++)
            {
                var agent = _agents[index];
                var renderer = index < _agentRenderers.Count ? _agentRenderers[index] : null;
                if (agent == null || renderer == null)
                {
                    continue;
                }

                Material material;
                if (agent.PlanningDeferred)
                {
                    QueuedAgents++;
                    material = _queuedMaterial;
                }
                else if (agent.CurrentAction != null)
                {
                    ActiveAgents++;
                    material = _activeMaterial;
                }
                else if (agent.LastCompletedGoal != null)
                {
                    CompletedAgents++;
                    material = _completedMaterial;
                    if (_mode == GoapBenchmarkMode.Visual)
                    {
                        agent.transform.localPosition = _displayPositions[index];
                        agent.transform.localRotation = Quaternion.identity;
                    }
                }
                else
                {
                    material = _idleMaterial;
                }

                if (_mode == GoapBenchmarkMode.Visual &&
                    material != null && renderer.sharedMaterial != material)
                {
                    renderer.sharedMaterial = material;
                }
            }
        }

        private void ApplyVisualizationMode()
        {
            var visible = _mode == GoapBenchmarkMode.Visual;
            foreach (var renderer in _agentRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }

            foreach (var renderer in _environmentRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private static Mesh GetCapsuleMesh()
        {
            var template = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            template.name = "GOAP Benchmark Mesh Template";
            template.SetActive(false);
            var mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Destroy(template);
            return mesh;
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0d;
            }

            var index = Mathf.Clamp(
                Mathf.CeilToInt((float)(percentile * sortedValues.Count)) - 1,
                0,
                sortedValues.Count - 1);
            return sortedValues[index];
        }

        private void OnGUI()
        {
            var scheduler = SchedulerMetrics;
            GUILayout.BeginArea(new Rect(12f, 12f, 460f, 210f), GUI.skin.box);
            GUILayout.Label($"GOAP Benchmark: {_agentCount} agents | {_mode}");
            GUILayout.Label($"Planned agents: {PlannedAgents}/{_agents.Count} | samples: {ObservedPlanCount}");
            GUILayout.Label(
                $"States: {QueuedAgents} queued | {ActiveAgents} active | {CompletedAgents} completed");
            GUILayout.Label(
                $"Search: {AveragePlanningMilliseconds:0.###} avg | {MedianPlanningMilliseconds:0.###} median | " +
                $"{P95PlanningMilliseconds:0.###} p95 ms");
            GUILayout.Label($"Expanded states: {AverageExpandedStates:0.#} avg | {MaximumExpandedStates} max");
            GUILayout.Label($"Frame: {AverageFrameMilliseconds:0.##} ms | {FramesPerSecond:0.#} FPS");
            GUILayout.Label(
                $"Scheduler: {scheduler.GrantedThisFrame} planned | {scheduler.QueuedRequests} waiting");
            GUILayout.Label(
                $"Peak planning: {scheduler.PeakPlanningMillisecondsPerFrame:0.###} ms/frame | " +
                $"deferred total: {scheduler.TotalDeferred}");
            GUILayout.EndArea();
        }
    }
}
