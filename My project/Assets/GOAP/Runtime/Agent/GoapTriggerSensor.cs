using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [RequireComponent(typeof(Collider))]
    public sealed class GoapTriggerSensor : GoapSensorBehaviour
    {
        [SerializeField] private GoapFact _fact = null;
        [SerializeField] private LayerMask _layers = ~0;
        [SerializeField] private string _requiredTag = string.Empty;

        private readonly HashSet<Collider> _inside = new();

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        private void OnDisable()
        {
            _inside.Clear();
            RequestRefresh();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Matches(other))
            {
                _inside.Add(other);
                RequestRefresh();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_inside.Remove(other))
            {
                RequestRefresh();
            }
        }

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            _inside.RemoveWhere(item => item == null || !item.enabled || !item.gameObject.activeInHierarchy);
            agent.SetFact(_fact, _inside.Count > 0);
        }

        private bool Matches(Collider other)
        {
            return other != null &&
                   (_layers.value & (1 << other.gameObject.layer)) != 0 &&
                   (string.IsNullOrWhiteSpace(_requiredTag) || other.CompareTag(_requiredTag));
        }
    }
}
