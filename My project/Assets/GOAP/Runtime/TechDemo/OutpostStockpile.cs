using System;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostStockpile : MonoBehaviour
    {
        [SerializeField, Min(0)] private int _wood = 4;
        [SerializeField, Min(0)] private int _food = 8;

        public int Wood => _wood;
        public int Food => _food;
        public event Action Changed;

        public void Configure(int wood, int food)
        {
            _wood = Mathf.Max(0, wood);
            _food = Mathf.Max(0, food);
        }

        public void Add(OutpostResourceKind kind, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (kind == OutpostResourceKind.Wood)
            {
                _wood += amount;
            }
            else
            {
                _food += amount;
            }

            Changed?.Invoke();
        }

        public bool TryTake(OutpostResourceKind kind, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (kind == OutpostResourceKind.Wood)
            {
                if (_wood < amount)
                {
                    return false;
                }

                _wood -= amount;
            }
            else
            {
                if (_food < amount)
                {
                    return false;
                }

                _food -= amount;
            }

            Changed?.Invoke();
            return true;
        }
    }
}
