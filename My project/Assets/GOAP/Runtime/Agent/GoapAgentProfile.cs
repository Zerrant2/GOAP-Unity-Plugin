using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Agent Profile", fileName = "New GOAP Agent Profile")]
    public sealed class GoapAgentProfile : ScriptableObject
    {
        [SerializeField] private GoapDomain _domain;
        [SerializeField] private List<GoapActionDefinition> _actions = new();
        [SerializeField] private List<GoapGoalDefinition> _goals = new();
        [SerializeField] private List<GoapFactValueReference> _initialFacts = new();
        [SerializeField] private List<GoapProfileSensorDefinition> _sensors = new();
        [SerializeField, Min(0.05f)] private float _decisionInterval = 0.2f;
        [SerializeField] private GoapPlannerSettings _plannerSettings = new()
        {
            MaxExpandedStates = 5000,
            MaxPlanDepth = 32,
            MaxPlanningMilliseconds = 10f
        };
        [SerializeField] private bool _logDecisions;

        public GoapDomain Domain => _domain;
        public IReadOnlyList<GoapActionDefinition> Actions => _actions.Count == 0 && _domain != null ? _domain.Actions : _actions;
        public IReadOnlyList<GoapGoalDefinition> Goals => _goals.Count == 0 && _domain != null ? _domain.Goals : _goals;
        public IReadOnlyList<GoapFactValueReference> InitialFacts => _initialFacts;
        public IReadOnlyList<GoapProfileSensorDefinition> Sensors => _sensors;
        public float DecisionInterval => Mathf.Max(0.05f, _decisionInterval);
        public GoapPlannerSettings PlannerSettings => _plannerSettings;
        public bool LogDecisions => _logDecisions;

        public void Configure(
            GoapDomain domain,
            IEnumerable<GoapActionDefinition> actions = null,
            IEnumerable<GoapGoalDefinition> goals = null,
            float decisionInterval = 0.2f,
            bool logDecisions = false,
            IEnumerable<GoapFactValueReference> initialFacts = null,
            IEnumerable<GoapProfileSensorDefinition> sensors = null)
        {
            _domain = domain;
            _actions = actions == null ? new List<GoapActionDefinition>() : new List<GoapActionDefinition>(actions);
            _goals = goals == null ? new List<GoapGoalDefinition>() : new List<GoapGoalDefinition>(goals);
            _initialFacts = initialFacts == null
                ? new List<GoapFactValueReference>()
                : new List<GoapFactValueReference>(initialFacts);
            _sensors = sensors == null
                ? new List<GoapProfileSensorDefinition>()
                : new List<GoapProfileSensorDefinition>(sensors);
            _decisionInterval = Mathf.Max(0.05f, decisionInterval);
            _plannerSettings = GoapPlannerSettings.Default;
            _logDecisions = logDecisions;
        }
    }
}
