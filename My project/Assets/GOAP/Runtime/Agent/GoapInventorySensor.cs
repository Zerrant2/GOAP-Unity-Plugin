using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapInventoryFactBinding
    {
        [SerializeField] private string _itemId;
        [SerializeField] private GoapFact _fact;
        [SerializeField, Min(1)] private int _requiredAmount;

        public string ItemId => _itemId;
        public GoapFact Fact => _fact;
        public int RequiredAmount => Mathf.Max(1, _requiredAmount);

        public GoapInventoryFactBinding(string itemId, GoapFact fact, int requiredAmount = 1)
        {
            _itemId = itemId;
            _fact = fact;
            _requiredAmount = Mathf.Max(1, requiredAmount);
        }
    }

    [RequireComponent(typeof(GoapInventory))]
    public sealed class GoapInventorySensor : GoapSensorBehaviour
    {
        [SerializeField] private List<GoapInventoryFactBinding> _bindings = new();

        private GoapInventory _inventory;

        private void Awake()
        {
            _inventory = GetComponent<GoapInventory>();
            _inventory.Changed += OnInventoryChanged;
        }

        private void OnDestroy()
        {
            if (_inventory != null)
            {
                _inventory.Changed -= OnInventoryChanged;
            }
        }

        public void Configure(IEnumerable<GoapInventoryFactBinding> bindings)
        {
            _bindings = bindings == null
                ? new List<GoapInventoryFactBinding>()
                : new List<GoapInventoryFactBinding>(bindings);
        }

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            _inventory ??= GetComponent<GoapInventory>();
            foreach (var binding in _bindings)
            {
                if (binding.Fact == null)
                {
                    continue;
                }

                var amount = _inventory.GetAmount(binding.ItemId);
                switch (binding.Fact.ValueType)
                {
                    case GoapFactType.Integer:
                        agent.SetFact(binding.Fact, amount);
                        break;
                    case GoapFactType.Float:
                        agent.SetFact(binding.Fact, (float)amount);
                        break;
                    default:
                        agent.SetFact(binding.Fact, amount >= binding.RequiredAmount);
                        break;
                }
            }
        }

        private void OnInventoryChanged(string itemId, int amount)
        {
            RequestRefresh();
        }
    }
}
