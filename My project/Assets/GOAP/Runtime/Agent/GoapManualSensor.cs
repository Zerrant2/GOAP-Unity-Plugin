using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapManualFactBinding
    {
        [SerializeField] private GoapFact _fact;
        [SerializeField] private bool _booleanValue;
        [SerializeField] private int _integerValue;
        [SerializeField] private float _floatValue;

        public GoapFact Fact => _fact;
        public GoapValue Value => _fact == null
            ? GoapValue.From(false)
            : _fact.ValueType switch
            {
                GoapFactType.Integer => GoapValue.From(_integerValue),
                GoapFactType.Float => GoapValue.From(_floatValue),
                GoapFactType.Enum => GoapValue.FromEnum(_fact.NormalizeEnumIndex(_integerValue)),
                _ => GoapValue.From(_booleanValue)
            };

        public GoapManualFactBinding(GoapFact fact, GoapValue value)
        {
            _fact = fact;
            _booleanValue = value.Boolean;
            _integerValue = value.Integer;
            _floatValue = value.Float;
        }
    }

    public sealed class GoapManualSensor : GoapSensorBehaviour
    {
        [SerializeField] private List<GoapManualFactBinding> _bindings = new();

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            foreach (var binding in _bindings)
            {
                agent.SetFact(binding.Fact, binding.Value);
            }
        }
    }
}
