using System;
using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapExecutionMode
    {
        Custom,
        Wait,
        SmartObjectInteraction,
        Sequence
    }

    public enum GoapInventoryOperation
    {
        None,
        Add,
        Remove
    }

    [Serializable]
    public struct GoapBuiltInActionSettings
    {
        [SerializeField] private GoapExecutionMode _mode;
        [SerializeField] private string _targetCategory;
        [SerializeField] private bool _moveToTarget;
        [SerializeField] private bool _reserveTarget;
        [SerializeField] private bool _consumeTarget;
        [SerializeField] private bool _useNavMesh;
        [SerializeField, Min(0.1f)] private float _moveSpeed;
        [SerializeField, Min(0.1f)] private float _interactionRange;
        [SerializeField, Min(0f)] private float _duration;
        [SerializeField] private GoapInventoryOperation _inventoryOperation;
        [SerializeField] private string _inventoryItemId;
        [SerializeField, Min(1)] private int _inventoryAmount;
        [SerializeField] private string _animatorTrigger;

        public GoapExecutionMode Mode => _mode;
        public string TargetCategory => _targetCategory;
        public bool MoveToTarget => _moveToTarget;
        public bool ReserveTarget => _reserveTarget;
        public bool ConsumeTarget => _consumeTarget;
        public bool UseNavMesh => _useNavMesh;
        public float MoveSpeed => Mathf.Max(0.1f, _moveSpeed);
        public float InteractionRange => Mathf.Max(0.1f, _interactionRange);
        public float Duration => Mathf.Max(0f, _duration);
        public GoapInventoryOperation InventoryOperation => _inventoryOperation;
        public string InventoryItemId => _inventoryItemId;
        public int InventoryAmount => Mathf.Max(1, _inventoryAmount);
        public string AnimatorTrigger => _animatorTrigger;

        public bool IsConfigured => _mode != GoapExecutionMode.Custom;

        public static GoapBuiltInActionSettings Wait(float duration)
        {
            return new GoapBuiltInActionSettings
            {
                _mode = GoapExecutionMode.Wait,
                _duration = Mathf.Max(0f, duration),
                _moveSpeed = 3.5f,
                _interactionRange = 1.1f,
                _inventoryAmount = 1,
                _animatorTrigger = string.Empty,
                _useNavMesh = false
            };
        }

        public static GoapBuiltInActionSettings Interact(
            string category,
            float duration = 0.5f,
            bool consumeTarget = false,
            GoapInventoryOperation inventoryOperation = GoapInventoryOperation.None,
            string itemId = "",
            int amount = 1)
        {
            return new GoapBuiltInActionSettings
            {
                _mode = GoapExecutionMode.SmartObjectInteraction,
                _targetCategory = category,
                _moveToTarget = true,
                _reserveTarget = true,
                _consumeTarget = consumeTarget,
                _moveSpeed = 3.5f,
                _interactionRange = 1.1f,
                _duration = Mathf.Max(0f, duration),
                _inventoryOperation = inventoryOperation,
                _inventoryItemId = itemId,
                _inventoryAmount = Mathf.Max(1, amount),
                _animatorTrigger = string.Empty,
                _useNavMesh = false
            };
        }

        public static GoapBuiltInActionSettings Sequence()
        {
            return new GoapBuiltInActionSettings
            {
                _mode = GoapExecutionMode.Sequence,
                _moveSpeed = 3.5f,
                _interactionRange = 1.1f,
                _inventoryAmount = 1
            };
        }
    }
}
