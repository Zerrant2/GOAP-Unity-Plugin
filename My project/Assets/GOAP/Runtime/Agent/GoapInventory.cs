using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public struct GoapInventoryEntry
    {
        [SerializeField] private string _itemId;
        [SerializeField, Min(0)] private int _amount;

        public string ItemId => _itemId;
        public int Amount => Mathf.Max(0, _amount);

        public GoapInventoryEntry(string itemId, int amount)
        {
            _itemId = itemId;
            _amount = Mathf.Max(0, amount);
        }

        public GoapInventoryEntry WithAmount(int amount)
        {
            return new GoapInventoryEntry(_itemId, amount);
        }
    }

    [DisallowMultipleComponent]
    public sealed class GoapInventory : MonoBehaviour
    {
        [SerializeField] private List<GoapInventoryEntry> _items = new();

        public IReadOnlyList<GoapInventoryEntry> Items => _items;
        public event Action<string, int> Changed;

        public int GetAmount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return 0;
            }

            for (var index = 0; index < _items.Count; index++)
            {
                if (string.Equals(_items[index].ItemId, itemId, StringComparison.Ordinal))
                {
                    return _items[index].Amount;
                }
            }

            return 0;
        }

        public void Add(string itemId, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            {
                return;
            }

            SetAmount(itemId, GetAmount(itemId) + amount);
        }

        public bool Remove(string itemId, int amount = 1)
        {
            if (amount <= 0 || GetAmount(itemId) < amount)
            {
                return false;
            }

            SetAmount(itemId, GetAmount(itemId) - amount);
            return true;
        }

        public void SetAmount(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            amount = Mathf.Max(0, amount);
            for (var index = 0; index < _items.Count; index++)
            {
                if (!string.Equals(_items[index].ItemId, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_items[index].Amount == amount)
                {
                    return;
                }

                _items[index] = _items[index].WithAmount(amount);
                Changed?.Invoke(itemId, amount);
                return;
            }

            _items.Add(new GoapInventoryEntry(itemId, amount));
            Changed?.Invoke(itemId, amount);
        }
    }
}
