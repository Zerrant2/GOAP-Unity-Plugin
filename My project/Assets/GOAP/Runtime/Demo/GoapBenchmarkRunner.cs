using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace Practice.GOAP.Demo
{
    public sealed class GoapBenchmarkRunner : MonoBehaviour
    {
        private static readonly ProfilerMarker SpawnMarker = new("GOAP.Benchmark.SpawnAgents");

        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField, Min(1)] private int _agentCount = 100;
        [SerializeField, Min(1f)] private float _spacing = 1.25f;

        private readonly List<GoapAgent> _agents = new();
        private float _nextMetricsTime;
        private double _averagePlanningMilliseconds;
        private int _plannedAgents;

        public IReadOnlyList<GoapAgent> Agents => _agents;
        public double AveragePlanningMilliseconds => _averagePlanningMilliseconds;

        public void Configure(GoapAgentProfile profile, int agentCount)
        {
            _profile = profile;
            _agentCount = Mathf.Max(1, agentCount);
        }

        private void Start()
        {
            using var marker = SpawnMarker.Auto();
            var columns = Mathf.CeilToInt(Mathf.Sqrt(_agentCount));
            for (var index = 0; index < _agentCount; index++)
            {
                var agentObject = new GameObject($"Benchmark Agent {index + 1}");
                agentObject.transform.SetParent(transform, false);
                agentObject.transform.localPosition = new Vector3(
                    (index % columns) * _spacing,
                    0f,
                    (index / columns) * _spacing);
                agentObject.AddComponent<GoapInventory>();
                var authoring = agentObject.AddComponent<GoapAgentAuthoring>();
                authoring.Configure(_profile);
                _agents.Add(agentObject.GetComponent<GoapAgent>());
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextMetricsTime)
            {
                return;
            }

            _nextMetricsTime = Time.unscaledTime + 0.5f;
            var plans = _agents
                .Where(agent => agent != null && agent.LastPlan != null)
                .Select(agent => agent.LastPlan.PlanningMilliseconds)
                .ToArray();
            _plannedAgents = plans.Length;
            _averagePlanningMilliseconds = plans.Length == 0 ? 0d : plans.Average();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 420f, 110f), GUI.skin.box);
            GUILayout.Label($"GOAP Benchmark: {_agentCount} agents");
            GUILayout.Label($"Plans built: {_plannedAgents}/{_agents.Count}");
            GUILayout.Label($"Average search: {_averagePlanningMilliseconds:0.###} ms");
            GUILayout.EndArea();
        }
    }
}
