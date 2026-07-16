using System.Collections.Generic;

namespace Practice.GOAP
{
    public sealed class GoapWorldState
    {
        private readonly IReadOnlyDictionary<GoapFact, int> _factIndices;
        private readonly GoapValue[] _indexedValues;
        private readonly Dictionary<GoapFact, GoapValue> _dynamicValues;

        public int Count => (_indexedValues?.Length ?? 0) + _dynamicValues.Count;

        public GoapWorldState()
        {
            _dynamicValues = new Dictionary<GoapFact, GoapValue>();
        }

        internal GoapWorldState(
            IReadOnlyList<GoapFact> facts,
            IReadOnlyDictionary<GoapFact, int> factIndices)
        {
            _factIndices = factIndices;
            _indexedValues = new GoapValue[facts.Count];
            _dynamicValues = new Dictionary<GoapFact, GoapValue>();
            for (var index = 0; index < facts.Count; index++)
            {
                _indexedValues[index] = facts[index].DefaultTypedValue;
            }
        }

        private GoapWorldState(
            IReadOnlyDictionary<GoapFact, int> factIndices,
            GoapValue[] indexedValues,
            Dictionary<GoapFact, GoapValue> dynamicValues)
        {
            _factIndices = factIndices;
            _indexedValues = indexedValues;
            _dynamicValues = dynamicValues;
        }

        public bool Get(GoapFact fact)
        {
            return GetValue(fact).Boolean;
        }

        public int GetInteger(GoapFact fact)
        {
            return GetValue(fact).Integer;
        }

        public float GetFloat(GoapFact fact)
        {
            return GetValue(fact).Float;
        }

        public int GetEnumIndex(GoapFact fact)
        {
            return GetValue(fact).Integer;
        }

        public GoapValue GetValue(GoapFact fact)
        {
            if (fact == null)
            {
                return GoapValue.From(false);
            }

            if (_factIndices != null && _factIndices.TryGetValue(fact, out var index))
            {
                return _indexedValues[index];
            }

            return _dynamicValues.TryGetValue(fact, out var value) ? value : fact.DefaultTypedValue;
        }

        public bool Set(GoapFact fact, bool value)
        {
            return SetValue(fact, GoapValue.From(value));
        }

        public bool Set(GoapFact fact, int value)
        {
            return SetValue(fact, GoapValue.From(value));
        }

        public bool Set(GoapFact fact, float value)
        {
            return SetValue(fact, GoapValue.From(value));
        }

        public bool SetEnum(GoapFact fact, int index)
        {
            return SetValue(fact, GoapValue.FromEnum(index));
        }

        public bool SetValue(GoapFact fact, GoapValue value)
        {
            if (fact == null)
            {
                return false;
            }

            value = value.ConvertTo(fact.ValueType);
            if (_factIndices != null && _factIndices.TryGetValue(fact, out var index))
            {
                if (_indexedValues[index].Equals(value))
                {
                    return false;
                }

                _indexedValues[index] = value;
                return true;
            }

            if (_dynamicValues.TryGetValue(fact, out var current) && current.Equals(value))
            {
                return false;
            }

            _dynamicValues[fact] = value;
            return true;
        }

        public bool Satisfies(IReadOnlyList<GoapCondition> conditions)
        {
            if (conditions == null)
            {
                return true;
            }

            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                if (!condition.IsValid || !condition.Matches(GetValue(condition.Fact)))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Apply(IReadOnlyList<GoapCondition> effects)
        {
            var changed = false;
            if (effects == null)
            {
                return false;
            }

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                if (effect.IsValid)
                {
                    changed |= SetValue(effect.Fact, effect.Apply(GetValue(effect.Fact)));
                }
            }

            return changed;
        }

        public GoapWorldState Clone()
        {
            return new GoapWorldState(
                _factIndices,
                _indexedValues == null ? null : (GoapValue[])_indexedValues.Clone(),
                new Dictionary<GoapFact, GoapValue>(_dynamicValues));
        }

        internal GoapStateKey BuildKey(IReadOnlyList<GoapFact> orderedFacts)
        {
            return new GoapStateKey(this, orderedFacts);
        }
    }
}
