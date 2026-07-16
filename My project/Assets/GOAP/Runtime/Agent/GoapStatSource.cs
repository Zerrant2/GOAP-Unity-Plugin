using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public sealed class GoapStat
    {
        [SerializeField] private string _id;
        [SerializeField] private float _value;
        [SerializeField] private float _minimum;
        [SerializeField] private float _maximum = 100f;

        public string Id => _id;
        public float Value => _value;

        public GoapStat(string id, float value, float minimum = 0f, float maximum = 100f)
        {
            _id = id;
            _minimum = minimum;
            _maximum = Mathf.Max(minimum, maximum);
            _value = Mathf.Clamp(value, _minimum, _maximum);
        }

        public bool Set(float value)
        {
            var next = Mathf.Clamp(value, _minimum, _maximum);
            if (Mathf.Approximately(next, _value))
            {
                return false;
            }

            _value = next;
            return true;
        }
    }

    public sealed class GoapStatSource : MonoBehaviour
    {
        [SerializeField] private List<GoapStat> _stats = new();

        public event Action<string, float> Changed;

        public bool TryGetValue(string id, out float value)
        {
            foreach (var stat in _stats)
            {
                if (stat != null && string.Equals(stat.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    value = stat.Value;
                    return true;
                }
            }

            value = 0f;
            return false;
        }

        public bool SetValue(string id, float value)
        {
            foreach (var stat in _stats)
            {
                if (stat == null || !string.Equals(stat.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (stat.Set(value))
                {
                    Changed?.Invoke(stat.Id, stat.Value);
                    return true;
                }

                return false;
            }

            _stats.Add(new GoapStat(id, value));
            Changed?.Invoke(id, value);
            return true;
        }

        public bool AddValue(string id, float delta)
        {
            return TryGetValue(id, out var value) && SetValue(id, value + delta);
        }
    }
}
