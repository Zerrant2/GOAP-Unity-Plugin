using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapSmartObjectFactBinding
    {
        [SerializeField] private string _category;
        [SerializeField] private GoapFact _fact;
        [SerializeField, Min(0f)] private float _maxDistance;

        public string Category => _category;
        public GoapFact Fact => _fact;
        public float MaxDistance => _maxDistance <= 0f ? float.PositiveInfinity : _maxDistance;

        public GoapSmartObjectFactBinding(string category, GoapFact fact, float maxDistance = 0f)
        {
            _category = category;
            _fact = fact;
            _maxDistance = Mathf.Max(0f, maxDistance);
        }
    }

    public sealed class GoapSmartObjectSensor : GoapSensorBehaviour
    {
        [SerializeField] private List<GoapSmartObjectFactBinding> _bindings = new();

        public void Configure(IEnumerable<GoapSmartObjectFactBinding> bindings)
        {
            _bindings = bindings == null
                ? new List<GoapSmartObjectFactBinding>()
                : new List<GoapSmartObjectFactBinding>(bindings);
        }

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            foreach (var binding in _bindings)
            {
                if (binding.Fact == null)
                {
                    continue;
                }

                var count = GoapSmartObject.CountAvailable(
                    binding.Category,
                    transform.position,
                    agent,
                    binding.MaxDistance);
                switch (binding.Fact.ValueType)
                {
                    case GoapFactType.Integer:
                        agent.SetFact(binding.Fact, count);
                        break;
                    case GoapFactType.Float:
                        agent.SetFact(binding.Fact, (float)count);
                        break;
                    default:
                        agent.SetFact(binding.Fact, count > 0);
                        break;
                }
            }
        }
    }
}
