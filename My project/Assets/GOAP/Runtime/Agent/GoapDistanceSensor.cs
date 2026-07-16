using UnityEngine;

namespace Practice.GOAP
{
    public sealed class GoapDistanceSensor : GoapSensorBehaviour
    {
        [SerializeField] private Transform _target = null;
        [SerializeField] private GoapFact _distanceFact = null;
        [SerializeField] private GoapFact _withinRangeFact = null;
        [SerializeField, Min(0f)] private float _range = 5f;

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            var distance = _target == null
                ? float.PositiveInfinity
                : Vector3.Distance(transform.position, _target.position);
            if (_distanceFact != null)
            {
                switch (_distanceFact.ValueType)
                {
                    case GoapFactType.Integer:
                        agent.SetFact(
                            _distanceFact,
                            float.IsPositiveInfinity(distance) ? int.MaxValue : Mathf.RoundToInt(distance));
                        break;
                    case GoapFactType.Float:
                        agent.SetFact(_distanceFact, distance);
                        break;
                    default:
                        agent.SetFact(_distanceFact, _target != null);
                        break;
                }
            }

            if (_withinRangeFact != null)
            {
                agent.SetFact(_withinRangeFact, distance <= _range);
            }
        }
    }
}
