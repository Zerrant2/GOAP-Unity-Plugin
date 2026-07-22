using System;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostCamp : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _maxHealth = 250f;
        [SerializeField, Min(0f)] private float _health = 250f;

        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public bool IsDamaged => _health < _maxHealth - 0.01f;
        public bool IsDestroyed => _health <= 0f;
        public event Action Changed;

        public void Configure(float maxHealth)
        {
            _maxHealth = Mathf.Max(1f, maxHealth);
            _health = _maxHealth;
        }

        public void Damage(float amount)
        {
            var next = Mathf.Max(0f, _health - Mathf.Max(0f, amount));
            if (Mathf.Approximately(next, _health))
            {
                return;
            }

            _health = next;
            Changed?.Invoke();
        }

        public void Repair(float amount)
        {
            var next = Mathf.Min(_maxHealth, _health + Mathf.Max(0f, amount));
            if (Mathf.Approximately(next, _health))
            {
                return;
            }

            _health = next;
            Changed?.Invoke();
        }
    }
}
