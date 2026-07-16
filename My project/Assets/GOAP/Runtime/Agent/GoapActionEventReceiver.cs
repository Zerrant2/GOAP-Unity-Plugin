using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Practice.GOAP
{
    [Serializable]
    public sealed class GoapNamedEvent
    {
        [SerializeField] private string _id;
        [SerializeField] private UnityEvent _event = new();

        public string Id => _id;

        public GoapNamedEvent()
        {
            _id = string.Empty;
            _event = new UnityEvent();
        }

        public bool InvokeIfMatches(string id)
        {
            if (!string.Equals(_id, id, StringComparison.Ordinal))
            {
                return false;
            }

            _event.Invoke();
            return true;
        }
    }

    public sealed class GoapActionEventReceiver : MonoBehaviour
    {
        [SerializeField] private List<GoapNamedEvent> _events = new();

        public bool Invoke(string id)
        {
            foreach (var namedEvent in _events)
            {
                if (namedEvent != null && namedEvent.InvokeIfMatches(id))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
