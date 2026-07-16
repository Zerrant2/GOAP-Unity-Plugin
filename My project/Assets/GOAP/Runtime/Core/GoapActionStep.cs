using System;
using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapActionStepKind
    {
        FindSmartObject,
        ReserveTarget,
        MoveToTarget,
        Interact,
        Wait,
        ConsumeTarget,
        ReleaseTarget,
        InventoryAdd,
        InventoryRemove,
        SetFact,
        AddFact,
        SubtractFact,
        TriggerAnimation,
        InvokeEvent
    }

    [Serializable]
    public sealed class GoapActionStep
    {
        [SerializeField] private GoapActionStepKind _kind;
        [SerializeField] private string _targetCategory;
        [SerializeField] private string _targetId;
        [SerializeField] private bool _useNavMesh;
        [SerializeField, Min(0.1f)] private float _moveSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float _interactionRange = 1.1f;
        [SerializeField, Min(0f)] private float _duration = 0.5f;
        [SerializeField, Min(0.1f)] private float _timeout = 10f;
        [SerializeField] private string _itemId;
        [SerializeField, Min(1)] private int _amount = 1;
        [SerializeField] private GoapFactValueReference _factValue;
        [SerializeField] private string _eventId;

        public GoapActionStepKind Kind => _kind;
        public string TargetCategory => _targetCategory;
        public string TargetId => _targetId;
        public bool UseNavMesh => _useNavMesh;
        public float MoveSpeed => Mathf.Max(0.1f, _moveSpeed);
        public float InteractionRange => Mathf.Max(0.1f, _interactionRange);
        public float Duration => Mathf.Max(0f, _duration);
        public float Timeout => Mathf.Max(0.1f, _timeout);
        public string ItemId => _itemId;
        public int Amount => Mathf.Max(1, _amount);
        public GoapFactValueReference FactValue => _factValue;
        public string EventId => _eventId;

        public GoapActionStep(GoapActionStepKind kind)
        {
            _kind = kind;
            _targetCategory = string.Empty;
            _targetId = string.Empty;
            _useNavMesh = false;
            _moveSpeed = 3.5f;
            _interactionRange = 1.1f;
            _duration = 0.5f;
            _timeout = 10f;
            _itemId = string.Empty;
            _amount = 1;
            _factValue = default;
            _eventId = string.Empty;
        }

        public static GoapActionStep Find(string category)
        {
            var step = new GoapActionStep(GoapActionStepKind.FindSmartObject);
            step._targetCategory = category;
            return step;
        }

        public static GoapActionStep Reserve(float timeout = 10f)
        {
            var step = new GoapActionStep(GoapActionStepKind.ReserveTarget);
            step._timeout = Mathf.Max(0.1f, timeout);
            return step;
        }

        public static GoapActionStep Move(float range = 1.1f, float speed = 3.5f, bool useNavMesh = false)
        {
            var step = new GoapActionStep(GoapActionStepKind.MoveToTarget);
            step._interactionRange = Mathf.Max(0.1f, range);
            step._moveSpeed = Mathf.Max(0.1f, speed);
            step._useNavMesh = useNavMesh;
            return step;
        }

        public static GoapActionStep Wait(float duration)
        {
            var step = new GoapActionStep(GoapActionStepKind.Wait);
            step._duration = Mathf.Max(0f, duration);
            return step;
        }

        public static GoapActionStep Inventory(GoapActionStepKind kind, string itemId, int amount = 1)
        {
            var step = new GoapActionStep(kind);
            step._itemId = itemId;
            step._amount = Mathf.Max(1, amount);
            return step;
        }

        public static GoapActionStep Fact(GoapActionStepKind kind, GoapFactValueReference value)
        {
            var step = new GoapActionStep(kind);
            step._factValue = value;
            return step;
        }

        public static GoapActionStep Event(GoapActionStepKind kind, string eventId)
        {
            var step = new GoapActionStep(kind);
            step._eventId = eventId;
            return step;
        }
    }
}
