using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GoapSmartObject))]
    public sealed class OutpostResourceNode : MonoBehaviour
    {
        [SerializeField] private OutpostResourceKind _kind;
        [SerializeField, Min(1)] private int _capacity = 3;
        [SerializeField, Min(1)] private int _remaining = 3;
        [SerializeField, Min(1f)] private float _respawnDelay = 12f;

        private GoapSmartObject _smartObject;
        private float _respawnAt;

        public OutpostResourceKind Kind => _kind;
        public int Remaining => _remaining;
        public bool Available => _remaining > 0 && SmartObject.Available;
        public GoapSmartObject SmartObject => _smartObject != null
            ? _smartObject
            : _smartObject = GetComponent<GoapSmartObject>();

        public void Configure(OutpostResourceKind kind, int capacity, float respawnDelay)
        {
            _kind = kind;
            _capacity = Mathf.Max(1, capacity);
            _remaining = _capacity;
            _respawnDelay = Mathf.Max(1f, respawnDelay);
            SmartObject.Configure(
                kind == OutpostResourceKind.Wood ? OutpostIds.TreeCategory : OutpostIds.FoodCategory,
                false);
        }

        private void Update()
        {
            if (_remaining > 0 || Time.time < _respawnAt)
            {
                return;
            }

            _remaining = _capacity;
            SmartObject.SetAvailable(true);
        }

        public bool TryHarvest(GoapAgent agent)
        {
            if (_remaining <= 0 || !SmartObject.IsReservedBy(agent))
            {
                return false;
            }

            _remaining--;
            if (_remaining <= 0)
            {
                _respawnAt = Time.time + _respawnDelay;
                SmartObject.SetAvailable(false);
            }

            return true;
        }
    }
}
