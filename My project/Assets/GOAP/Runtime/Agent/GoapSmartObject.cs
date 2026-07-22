using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Practice.GOAP
{
    [DisallowMultipleComponent]
    public sealed class GoapSmartObject : MonoBehaviour
    {
        private static readonly List<GoapSmartObject> Instances = new();

        [SerializeField] private string _category = "Resource";
        [SerializeField, Min(1)] private int _capacity = 1;
        [SerializeField, Min(0.5f)] private float _reservationTimeout = 10f;
        [SerializeField] private bool _consumeOnUse;
        [SerializeField] private bool _hideWhenUnavailable = true;
        [SerializeField] private bool _available = true;
        [SerializeField] private UnityEvent _onInteract = new();

        private readonly Dictionary<GoapAgent, float> _reservations = new();
        private readonly Queue<ReservationRequest> _reservationQueue = new();

        public string Category => _category;
        public bool Available => _available && isActiveAndEnabled;
        public int Capacity => Mathf.Max(1, _capacity);
        public int ReservedCount => _reservations.Count;
        public int QueueCount => _reservationQueue.Count;
        public event Action<GoapSmartObject> AvailabilityChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            Instances.Clear();
        }

        private void OnEnable()
        {
            if (!Instances.Contains(this))
            {
                Instances.Add(this);
            }
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            _reservations.Clear();
            _reservationQueue.Clear();
        }

        public void Configure(string category, bool consumeOnUse, int capacity = 1)
        {
            _category = category;
            _consumeOnUse = consumeOnUse;
            _capacity = Mathf.Max(1, capacity);
            SetAvailable(true);
        }

        public bool IsAvailableTo(GoapAgent agent)
        {
            RemoveExpiredReservations();
            return Available && (_reservations.ContainsKey(agent) || _reservations.Count < Capacity);
        }

        public bool IsReservedBy(GoapAgent agent)
        {
            RemoveExpiredReservations();
            return agent != null && _reservations.ContainsKey(agent);
        }

        public bool TryReserve(GoapAgent agent)
        {
            if (agent == null || !IsAvailableTo(agent))
            {
                return false;
            }

            _reservations[agent] = Time.time + Mathf.Max(0.5f, _reservationTimeout);
            return true;
        }

        public bool RequestReservation(GoapAgent agent, float queueTimeout = 10f)
        {
            RemoveExpiredReservations();
            if (agent == null || !Available)
            {
                return false;
            }

            if (_reservations.ContainsKey(agent))
            {
                return RefreshReservation(agent);
            }

            if (_reservationQueue.Count == 0 && _reservations.Count < Capacity)
            {
                _reservations[agent] = Time.time + Mathf.Max(0.5f, _reservationTimeout);
                return true;
            }

            if (!IsQueued(agent))
            {
                _reservationQueue.Enqueue(new ReservationRequest(
                    agent,
                    Time.time + Mathf.Max(0.1f, queueTimeout)));
            }

            PromoteQueue();
            return _reservations.ContainsKey(agent);
        }

        public bool IsQueued(GoapAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            foreach (var request in _reservationQueue)
            {
                if (request.Agent == agent)
                {
                    return true;
                }
            }

            return false;
        }

        public int GetQueuePosition(GoapAgent agent)
        {
            RemoveExpiredReservations();
            if (agent == null)
            {
                return 0;
            }

            var position = 1;
            foreach (var request in _reservationQueue)
            {
                if (request.Agent == agent)
                {
                    return position;
                }

                position++;
            }

            return 0;
        }

        public void CancelReservationRequest(GoapAgent agent)
        {
            if (agent == null || _reservationQueue.Count == 0)
            {
                return;
            }

            var remaining = _reservationQueue.Count;
            while (remaining-- > 0)
            {
                var request = _reservationQueue.Dequeue();
                if (request.Agent != agent)
                {
                    _reservationQueue.Enqueue(request);
                }
            }
        }

        public bool RefreshReservation(GoapAgent agent)
        {
            if (!IsReservedBy(agent))
            {
                return false;
            }

            _reservations[agent] = Time.time + Mathf.Max(0.5f, _reservationTimeout);
            return true;
        }

        public void Release(GoapAgent agent)
        {
            if (agent != null)
            {
                _reservations.Remove(agent);
                CancelReservationRequest(agent);
                PromoteQueue();
            }
        }

        public bool Interact(GoapAgent agent)
        {
            RemoveExpiredReservations();
            if (!Available || (_reservations.Count > 0 && !IsReservedBy(agent)))
            {
                return false;
            }

            _onInteract.Invoke();
            return true;
        }

        public bool TryUse(GoapAgent agent)
        {
            RemoveExpiredReservations();
            if (!Available || (_reservations.Count > 0 && !IsReservedBy(agent)))
            {
                return false;
            }

            if (_consumeOnUse)
            {
                SetAvailable(false);
            }

            return true;
        }

        public void SetAvailable(bool available)
        {
            if (_available == available)
            {
                return;
            }

            _available = available;
            if (!available)
            {
                _reservations.Clear();
                _reservationQueue.Clear();
            }

            if (_hideWhenUnavailable)
            {
                foreach (var rendererComponent in GetComponentsInChildren<Renderer>(true))
                {
                    rendererComponent.enabled = available;
                }

                foreach (var colliderComponent in GetComponentsInChildren<Collider>(true))
                {
                    colliderComponent.enabled = available;
                }
            }

            AvailabilityChanged?.Invoke(this);
        }

        public static GoapSmartObject FindClosest(
            string category,
            Vector3 origin,
            GoapAgent agent = null,
            float maxDistance = float.PositiveInfinity,
            bool includeBusy = false)
        {
            GoapSmartObject closest = null;
            var bestDistance = maxDistance * maxDistance;
            foreach (var item in Instances)
            {
                if (item == null ||
                    !string.Equals(item.Category, category, StringComparison.Ordinal) ||
                    (!includeBusy && !item.IsAvailableTo(agent)) ||
                    (includeBusy && !item.Available))
                {
                    continue;
                }

                var distance = (item.transform.position - origin).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    closest = item;
                }
            }

            return closest;
        }

        public static int CountAvailable(string category, Vector3 origin, GoapAgent agent, float maxDistance)
        {
            var count = 0;
            var maxDistanceSquared = maxDistance <= 0f || float.IsPositiveInfinity(maxDistance)
                ? float.PositiveInfinity
                : maxDistance * maxDistance;
            foreach (var item in Instances)
            {
                if (item != null &&
                    string.Equals(item.Category, category, StringComparison.Ordinal) &&
                    item.IsAvailableTo(agent) &&
                    (item.transform.position - origin).sqrMagnitude <= maxDistanceSquared)
                {
                    count++;
                }
            }

            return count;
        }

        private void RemoveExpiredReservations()
        {
            List<GoapAgent> expired = null;
            foreach (var pair in _reservations)
            {
                if (pair.Key != null && pair.Value > Time.time)
                {
                    continue;
                }

                expired ??= new List<GoapAgent>();
                expired.Add(pair.Key);
            }

            if (expired != null)
            {
                foreach (var agent in expired)
                {
                    _reservations.Remove(agent);
                }
            }

            RemoveExpiredQueueRequests();
            PromoteQueue();
        }

        private void RemoveExpiredQueueRequests()
        {
            var remaining = _reservationQueue.Count;
            while (remaining-- > 0)
            {
                var request = _reservationQueue.Dequeue();
                if (request.Agent != null && request.ExpiresAt > Time.time)
                {
                    _reservationQueue.Enqueue(request);
                }
            }
        }

        private void PromoteQueue()
        {
            RemoveExpiredQueueRequests();
            while (Available && _reservations.Count < Capacity && _reservationQueue.Count > 0)
            {
                var request = _reservationQueue.Dequeue();
                if (request.Agent != null)
                {
                    _reservations[request.Agent] = Time.time + Mathf.Max(0.5f, _reservationTimeout);
                }
            }
        }

        private readonly struct ReservationRequest
        {
            public readonly GoapAgent Agent;
            public readonly float ExpiresAt;

            public ReservationRequest(GoapAgent agent, float expiresAt)
            {
                Agent = agent;
                ExpiresAt = expiresAt;
            }
        }
    }
}
