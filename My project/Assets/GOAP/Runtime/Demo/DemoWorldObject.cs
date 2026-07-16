using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP.Demo
{
    public enum DemoWorldObjectType
    {
        None,
        Food,
        Bed,
        Weapon,
        Enemy
    }

    [DisallowMultipleComponent]
    public sealed class DemoWorldObject : MonoBehaviour
    {
        private static readonly List<DemoWorldObject> Instances = new();

        [SerializeField] private DemoWorldObjectType _type;
        [SerializeField] private bool _consumeOnUse = true;
        [SerializeField] private bool _available = true;

        public DemoWorldObjectType Type => _type;
        public bool Available => _available && isActiveAndEnabled;

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
        }

        public void Configure(DemoWorldObjectType type, bool consumeOnUse)
        {
            _type = type;
            _consumeOnUse = consumeOnUse;
            _available = true;
        }

        public bool TryUse()
        {
            if (!Available)
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
            _available = available;
            foreach (var rendererComponent in GetComponentsInChildren<Renderer>(true))
            {
                rendererComponent.enabled = available;
            }

            foreach (var colliderComponent in GetComponentsInChildren<Collider>(true))
            {
                colliderComponent.enabled = available;
            }
        }

        public static bool IsAvailable(DemoWorldObjectType type)
        {
            return FindClosest(type, Vector3.zero) != null;
        }

        public static DemoWorldObject FindClosest(DemoWorldObjectType type, Vector3 origin)
        {
            DemoWorldObject closest = null;
            var bestDistance = float.PositiveInfinity;

            foreach (var item in Instances)
            {
                if (item == null || item._type != type || !item.Available)
                {
                    continue;
                }

                var distance = (item.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = item;
                }
            }

            return closest;
        }
    }
}
