using System.Collections.Generic;

namespace Practice.GOAP
{
    public sealed class GoapActionContext
    {
        private readonly HashSet<GoapFact> _handledEffects = new();
        private readonly Dictionary<GoapFact, GoapValue> _stagedFacts = new();

        public GoapAgent Agent { get; }
        public GoapActionDefinition Definition { get; }
        public GoapWorldState WorldState => Agent.WorldState;

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
    }
}
