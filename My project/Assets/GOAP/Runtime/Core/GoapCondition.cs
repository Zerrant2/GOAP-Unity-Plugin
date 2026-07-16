using System;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapCondition : IEquatable<GoapCondition>
    {
        [SerializeField] private GoapFact _fact;
        [SerializeField] private GoapComparison _comparison;
        [SerializeField] private bool _value;
        [SerializeField] private int _integerValue;
        [SerializeField] private float _floatValue;
        [SerializeField] private int _enumValue;
        [SerializeField] private GoapEffectOperation _effectOperation;

        public GoapFact Fact => _fact;
        public bool Value => _value;
        public GoapComparison Comparison => _comparison;
        public GoapEffectOperation EffectOperation => _effectOperation;
        public GoapValue ExpectedValue => _fact == null
            ? GoapValue.From(_value)
            : _fact.ValueType switch
            {
                GoapFactType.Integer => GoapValue.From(_integerValue),
                GoapFactType.Float => GoapValue.From(_floatValue),
                GoapFactType.Enum => GoapValue.FromEnum(_fact.NormalizeEnumIndex(_enumValue)),
                _ => GoapValue.From(_value)
            };
        public bool IsValid => _fact != null;

        public GoapCondition(GoapFact fact, bool value)
        {
            _fact = fact;
            _comparison = GoapComparison.Equal;
            _value = value;
            _integerValue = value ? 1 : 0;
            _floatValue = value ? 1f : 0f;
            _enumValue = value ? 1 : 0;
            _effectOperation = GoapEffectOperation.Set;
        }

        public GoapCondition(
            GoapFact fact,
            int value,
            GoapComparison comparison = GoapComparison.Equal,
            GoapEffectOperation effectOperation = GoapEffectOperation.Set)
        {
            _fact = fact;
            _comparison = comparison;
            _value = value != 0;
            _integerValue = value;
            _floatValue = value;
            _enumValue = value;
            _effectOperation = effectOperation;
        }

        public GoapCondition(
            GoapFact fact,
            float value,
            GoapComparison comparison = GoapComparison.Equal,
            GoapEffectOperation effectOperation = GoapEffectOperation.Set)
        {
            _fact = fact;
            _comparison = comparison;
            _value = Math.Abs(value) > 0.0001f;
            _integerValue = (int)value;
            _floatValue = value;
            _enumValue = (int)value;
            _effectOperation = effectOperation;
        }

        public bool Matches(GoapValue currentValue)
        {
            return IsValid && currentValue.Matches(_comparison, ExpectedValue);
        }

        public GoapValue Apply(GoapValue currentValue)
        {
            return currentValue.Apply(_effectOperation, ExpectedValue);
        }

        public bool CanEstablish(GoapCondition desired)
        {
            if (!IsValid || !desired.IsValid || _fact != desired._fact)
            {
                return false;
            }

            if (_effectOperation != GoapEffectOperation.Set)
            {
                return _fact.ValueType == GoapFactType.Integer || _fact.ValueType == GoapFactType.Float;
            }

            return desired.Matches(Apply(_fact.DefaultTypedValue));
        }

        public bool Equals(GoapCondition other)
        {
            return _fact == other._fact &&
                   _comparison == other._comparison &&
                   ExpectedValue.Equals(other.ExpectedValue) &&
                   _effectOperation == other._effectOperation;
        }

        public override bool Equals(object obj)
        {
            return obj is GoapCondition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_fact, _comparison, ExpectedValue, _effectOperation);
        }

        public override string ToString()
        {
            if (_fact == null)
            {
                return "<missing fact>";
            }

            if (_effectOperation != GoapEffectOperation.Set)
            {
                var operation = _effectOperation == GoapEffectOperation.Add ? "+=" : "-=";
                return $"{_fact.DisplayName} {operation} {ExpectedValue}";
            }

            var comparison = _comparison switch
            {
                GoapComparison.Equal => "=",
                GoapComparison.NotEqual => "!=",
                GoapComparison.Less => "<",
                GoapComparison.LessOrEqual => "<=",
                GoapComparison.Greater => ">",
                GoapComparison.GreaterOrEqual => ">=",
                _ => "?"
            };
            return $"{_fact.DisplayName} {comparison} {ExpectedValue}";
        }
    }
}
