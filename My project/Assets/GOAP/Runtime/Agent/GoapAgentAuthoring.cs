using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GoapAgent), typeof(GoapBuiltInActionBehaviour), typeof(GoapProfileSensorBehaviour))]
    public sealed class GoapAgentAuthoring : MonoBehaviour
    {
        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField] private List<GoapFactValueReference> _initialFactOverrides = new();
        [SerializeField] private List<GoapNamedTarget> _namedTargets = new();
        [SerializeField] private bool _applyOnAwake = true;

        public GoapAgentProfile Profile => _profile;
        public IReadOnlyList<GoapFactValueReference> InitialFactOverrides => _initialFactOverrides;
        public IReadOnlyList<GoapNamedTarget> NamedTargets => _namedTargets;

        private void Awake()
        {
            if (_applyOnAwake)
            {
                ApplyProfile();
            }
        }

        public void Configure(
            GoapAgentProfile profile,
            IEnumerable<GoapFactValueReference> initialFactOverrides = null,
            IEnumerable<GoapNamedTarget> namedTargets = null)
        {
            _profile = profile;
            if (initialFactOverrides != null)
            {
                _initialFactOverrides = new List<GoapFactValueReference>(initialFactOverrides);
            }

            if (namedTargets != null)
            {
                _namedTargets = new List<GoapNamedTarget>(namedTargets);
            }

            ApplyProfile();
        }

        public Transform ResolveTarget(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            foreach (var namedTarget in _namedTargets)
            {
                if (string.Equals(namedTarget.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return namedTarget.Target;
                }
            }

            return null;
        }

        public void ApplyProfile()
        {
            if (_profile != null && TryGetComponent<GoapAgent>(out var agent))
            {
                agent.Configure(_profile);
            }
        }
    }

    [Serializable]
    public struct GoapNamedTarget
    {
        [SerializeField] private string _id;
        [SerializeField] private Transform _target;

        public string Id => _id;
        public Transform Target => _target;

        public GoapNamedTarget(string id, Transform target)
        {
            _id = id;
            _target = target;
        }
    }
}
