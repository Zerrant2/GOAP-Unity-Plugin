using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Goal", fileName = "New Goal")]
    public sealed class GoapGoalDefinition : GoapDefinition
    {
        [SerializeField] private int _priority = 1;
        [SerializeField] private List<GoapCondition> _activationConditions = new();
        [SerializeField] private List<GoapCondition> _desiredState = new();

        public int Priority => _priority;
        public IReadOnlyList<GoapCondition> ActivationConditions => _activationConditions;
        public IReadOnlyList<GoapCondition> DesiredState => _desiredState;

        public void Configure(
            string displayName,
            int priority,
            IEnumerable<GoapCondition> activationConditions,
            IEnumerable<GoapCondition> desiredState,
            string description = "")
        {
            SetIdentity(displayName, description);
            _priority = priority;
            _activationConditions = activationConditions == null
                ? new List<GoapCondition>()
                : new List<GoapCondition>(activationConditions);
            _desiredState = desiredState == null
                ? new List<GoapCondition>()
                : new List<GoapCondition>(desiredState);
        }

        public bool AddActivationCondition(GoapCondition condition)
        {
            if (!condition.IsValid || _activationConditions.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _activationConditions.Add(condition);
            return true;
        }

        public bool AddDesiredCondition(GoapCondition condition)
        {
            if (!condition.IsValid || _desiredState.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _desiredState.Add(condition);
            return true;
        }

        public bool RemoveActivationCondition(GoapFact fact)
        {
            return _activationConditions.RemoveAll(item => item.Fact == fact) > 0;
        }

        public bool RemoveDesiredCondition(GoapFact fact)
        {
            return _desiredState.RemoveAll(item => item.Fact == fact) > 0;
        }
    }
}
