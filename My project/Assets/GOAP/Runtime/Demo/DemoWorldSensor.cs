using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP.Demo
{
    [Serializable]
    public struct DemoWorldFactBinding
    {
        [SerializeField] private DemoWorldObjectType _objectType;
        [SerializeField] private GoapFact _fact;

        public DemoWorldObjectType ObjectType => _objectType;
        public GoapFact Fact => _fact;

        public DemoWorldFactBinding(DemoWorldObjectType objectType, GoapFact fact)
        {
            _objectType = objectType;
            _fact = fact;
        }
    }

    public sealed class DemoWorldSensor : GoapSensorBehaviour
    {
        [SerializeField] private List<DemoWorldFactBinding> _bindings = new();

        public void Configure(IEnumerable<DemoWorldFactBinding> bindings)
        {
            _bindings = bindings == null
                ? new List<DemoWorldFactBinding>()
                : new List<DemoWorldFactBinding>(bindings);
        }

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            foreach (var binding in _bindings)
            {
                agent.SetFact(binding.Fact, DemoWorldObject.IsAvailable(binding.ObjectType));
            }
        }
    }
}
