using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public sealed class GoapCompiledDomain
    {
        private readonly GoapFact[] _facts;
        private readonly GoapActionDefinition[] _actions;
        private readonly GoapGoalDefinition[] _goals;
        private readonly Dictionary<GoapFact, int> _factIndices;

        public IReadOnlyList<GoapFact> Facts => _facts;
        public IReadOnlyList<GoapActionDefinition> Actions => _actions;
        public IReadOnlyList<GoapGoalDefinition> Goals => _goals;
        public IReadOnlyDictionary<GoapFact, int> FactIndices => _factIndices;

        internal GoapCompiledDomain(GoapDomain source)
        {
            _facts = source.Facts.Where(item => item != null).ToArray();
            _actions = source.Actions.Where(item => item != null).ToArray();
            _goals = source.Goals.Where(item => item != null).ToArray();
            _factIndices = new Dictionary<GoapFact, int>(_facts.Length);
            for (var index = 0; index < _facts.Length; index++)
            {
                _factIndices[_facts[index]] = index;
            }
        }

        public bool TryGetFactIndex(GoapFact fact, out int index)
        {
            return _factIndices.TryGetValue(fact, out index);
        }

        public GoapWorldState CreateDefaultState()
        {
            return new GoapWorldState(_facts, _factIndices);
        }
    }
}
