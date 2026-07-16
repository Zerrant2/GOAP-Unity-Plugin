using System;
using System.Collections.Generic;

namespace Practice.GOAP
{
    internal sealed class MinHeap<T> where T : IComparable<T>
    {
        private readonly List<T> _items = new();

        public int Count => _items.Count;

        public void Push(T item)
        {
            _items.Add(item);
            var index = _items.Count - 1;

            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (_items[parent].CompareTo(_items[index]) <= 0)
                {
                    break;
                }

                (_items[parent], _items[index]) = (_items[index], _items[parent]);
                index = parent;
            }
        }

        public T Pop()
        {
            if (_items.Count == 0)
            {
                throw new InvalidOperationException("Cannot pop an empty heap.");
            }

            var root = _items[0];
            var lastIndex = _items.Count - 1;
            _items[0] = _items[lastIndex];
            _items.RemoveAt(lastIndex);

            var index = 0;
            while (true)
            {
                var left = index * 2 + 1;
                var right = left + 1;
                var smallest = index;

                if (left < _items.Count && _items[left].CompareTo(_items[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < _items.Count && _items[right].CompareTo(_items[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
                index = smallest;
            }

            return root;
        }
    }
}
