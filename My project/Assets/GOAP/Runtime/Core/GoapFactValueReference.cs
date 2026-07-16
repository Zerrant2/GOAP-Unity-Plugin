using System;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapFactValueReference
    {
        [SerializeField] private GoapFact _fact;
        [SerializeField] private bool _booleanValue;
        [SerializeField] private int _integerValue;
        [SerializeField] private float _floatValue;
        [SerializeField] private int _enumValue;

        public GoapFact Fact => _fact;
        public bool IsValid => _fact != null;
        public GoapValue Value => _fact == null
            ? GoapValue.From(false)
            : _fact.ValueType switch
            {
                GoapFactType.Integer => GoapValue.From(_integerValue),
                GoapFactType.Float => GoapValue.From(_floatValue),
                GoapFactType.Enum => GoapValue.FromEnum(_fact.NormalizeEnumIndex(_enumValue)),
                _ => GoapValue.From(_booleanValue)
            };

        public GoapFactValueReference(GoapFact fact, bool value)
        {
            _fact = fact;
            _booleanValue = value;
            _integerValue = value ? 1 : 0;
            _floatValue = value ? 1f : 0f;
            _enumValue = value ? 1 : 0;
        }

        public GoapFactValueReference(GoapFact fact, int value, bool isEnum = false)
        {
            _fact = fact;
            _booleanValue = value != 0;
            _integerValue = value;
            _floatValue = value;
            _enumValue = value;
        }

        public GoapFactValueReference(GoapFact fact, float value)
        {
            _fact = fact;
            _booleanValue = Math.Abs(value) > 0.0001f;
            _integerValue = (int)value;
            _floatValue = value;
            _enumValue = (int)value;
        }
    }
}
