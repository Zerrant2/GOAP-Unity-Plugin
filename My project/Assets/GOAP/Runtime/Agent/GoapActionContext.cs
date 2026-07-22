using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    public sealed class GoapActionContext
    {
        private readonly HashSet<GoapFact> _handledEffects = new();
        private readonly Dictionary<GoapFact, GoapValue> _stagedFacts = new();

        public GoapAgent Agent { get; }
        public GoapActionDefinition Definition { get; }
        public GoapWorldState WorldState => Agent.WorldState;
        public GoapSmartObject SmartObjectTarget { get; private set; }
        public Transform NamedTarget { get; private set; }
        public Transform Target => SmartObjectTarget != null ? SmartObjectTarget.transform : NamedTarget;

        public GoapActionContext(GoapAgent agent, GoapActionDefinition definition)
        {
            Agent = agent;
            Definition = definition;
        }

        public void MarkEffectHandled(GoapFact fact)
        {
            if (fact != null)
            {
                _handledEffects.Add(fact);
            }
        }

        public bool IsEffectHandled(GoapFact fact)
        {
            return fact != null && _handledEffects.Contains(fact);
        }

        public GoapValue GetFactValue(GoapFact fact)
        {
            return fact != null && _stagedFacts.TryGetValue(fact, out var value)
                ? value
                : WorldState.GetValue(fact);
        }

        public void StageFact(GoapFact fact, GoapValue value)
        {
            if (fact == null)
            {
                return;
            }

            _stagedFacts[fact] = value.ConvertTo(fact.ValueType);
            MarkEffectHandled(fact);
        }

        public void ApplyStagedFacts()
        {
            foreach (var pair in _stagedFacts)
            {
                Agent.SetFact(pair.Key, pair.Value);
            }
        }

        internal void SetTarget(GoapActionTargetDescriptor descriptor, Transform target)
        {
            SmartObjectTarget = descriptor.Mode == GoapActionTargetMode.SmartObjectCategory
                ? target != null ? target.GetComponent<GoapSmartObject>() : null
                : null;
            NamedTarget = descriptor.Mode == GoapActionTargetMode.NamedTarget ? target : null;
        }
    }
}
