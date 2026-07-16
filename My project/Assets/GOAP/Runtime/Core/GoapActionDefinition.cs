using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Action", fileName = "New Action")]
    public sealed class GoapActionDefinition : GoapDefinition
    {
        [SerializeField, Min(0.01f)] private float _cost = 1f;
        [SerializeField] private string _executorId;
        [SerializeField] private GoapBuiltInActionSettings _builtInExecution;
        [SerializeField] private List<GoapActionStep> _executionSteps = new();
        [SerializeField] private List<GoapCondition> _preconditions = new();
        [SerializeField] private List<GoapCondition> _effects = new();

        public float Cost => Mathf.Max(0.01f, _cost);
        public string ExecutorId => _executorId;
        public GoapBuiltInActionSettings BuiltInExecution => _builtInExecution;
        public IReadOnlyList<GoapActionStep> ExecutionSteps => _executionSteps;
        public bool UsesBuiltInExecutor => _builtInExecution.IsConfigured;
        public IReadOnlyList<GoapCondition> Preconditions => _preconditions;
        public IReadOnlyList<GoapCondition> Effects => _effects;

        public void Configure(
            string displayName,
            float cost,
            string executorId,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects,
            string description = "")
        {
            SetIdentity(displayName, description);
            _cost = Mathf.Max(0.01f, cost);
            _executorId = executorId;
            _builtInExecution = default;
            _executionSteps = new List<GoapActionStep>();
            _preconditions = preconditions == null ? new List<GoapCondition>() : new List<GoapCondition>(preconditions);
            _effects = effects == null ? new List<GoapCondition>() : new List<GoapCondition>(effects);
        }

        public void ConfigureBuiltInExecution(GoapBuiltInActionSettings settings)
        {
            _builtInExecution = settings;
        }

        public void ConfigureExecutionSteps(IEnumerable<GoapActionStep> steps)
        {
            _executionSteps = steps == null ? new List<GoapActionStep>() : new List<GoapActionStep>(steps);
            _builtInExecution = GoapBuiltInActionSettings.Sequence();
        }

        public bool AddPrecondition(GoapCondition condition)
        {
            if (!condition.IsValid || _preconditions.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _preconditions.Add(condition);
            return true;
        }

        public bool AddEffect(GoapCondition condition)
        {
            if (!condition.IsValid || _effects.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _effects.Add(condition);
            return true;
        }

        public bool RemovePrecondition(GoapFact fact)
        {
            return _preconditions.RemoveAll(item => item.Fact == fact) > 0;
        }

        public bool RemoveEffect(GoapFact fact)
        {
            return _effects.RemoveAll(item => item.Fact == fact) > 0;
        }
    }
}
