using System.Collections.Generic;
using System.Linq;
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
        [SerializeField, Min(0f)] private float _goalSwitchThreshold = 5f;
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
        public float GoalSwitchThreshold => Mathf.Max(0f, _goalSwitchThreshold);
        public GoapPlannerSettings PlannerSettings => _plannerSettings;
        public bool LogDecisions => _logDecisions;

        public void Configure(
            GoapDomain domain,
            IEnumerable<GoapActionDefinition> actions = null,
            IEnumerable<GoapGoalDefinition> goals = null,
            float decisionInterval = 0.2f,
            bool logDecisions = false,
            IEnumerable<GoapFactValueReference> initialFacts = null,
            IEnumerable<GoapProfileSensorDefinition> sensors = null,
            float goalSwitchThreshold = 5f)
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
            _goalSwitchThreshold = Mathf.Max(0f, goalSwitchThreshold);
            _plannerSettings = GoapPlannerSettings.Default;
            _logDecisions = logDecisions;
        }

        public bool SetInitialFact(GoapFactValueReference value)
        {
            if (!value.IsValid || _domain == null || !_domain.Facts.Contains(value.Fact))
            {
                return false;
            }

            var index = _initialFacts.FindIndex(item => item.Fact == value.Fact);
            if (index >= 0)
            {
                _initialFacts[index] = value;
            }
            else
            {
                _initialFacts.Add(value);
            }

            return true;
        }

        public bool RemoveInitialFact(GoapFact fact)
        {
            return fact != null && _initialFacts.RemoveAll(item => item.Fact == fact) > 0;
        }

        public bool SetSensor(GoapProfileSensorDefinition sensor)
        {
            if (sensor?.Fact == null || _domain == null || !_domain.Facts.Contains(sensor.Fact))
            {
                return false;
            }

            var index = _sensors.FindIndex(item => item != null && item.Fact == sensor.Fact);
            if (index >= 0)
            {
                _sensors[index] = sensor;
            }
            else
            {
                _sensors.Add(sensor);
            }

            return true;
        }

        public bool RemoveSensor(GoapFact fact)
        {
            return fact != null && _sensors.RemoveAll(item => item != null && item.Fact == fact) > 0;
        }
    }
}
