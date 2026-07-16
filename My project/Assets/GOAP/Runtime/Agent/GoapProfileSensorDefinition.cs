using System;
using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapProfileSensorKind
    {
        SmartObject,
        Inventory,
        Distance,
        Proximity,
        Stat,
        Time,
        ComponentProperty,
        Constant
    }

    [Serializable]
    public sealed class GoapProfileSensorDefinition
    {
        [SerializeField] private string _name = "Sensor";
        [SerializeField] private GoapProfileSensorKind _kind;
        [SerializeField] private GoapFact _fact;
        [SerializeField] private string _sourceId;
        [SerializeField] private string _targetId;
        [SerializeField] private string _requiredTag;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField, Min(0f)] private float _radius = 5f;
        [SerializeField] private GoapComparison _comparison = GoapComparison.GreaterOrEqual;
        [SerializeField] private float _threshold = 1f;
        [SerializeField] private float _scale = 1f;
        [SerializeField] private float _offset;
        [SerializeField] private string _componentType;
        [SerializeField] private string _memberName;
        [SerializeField] private GoapFactValueReference _constantValue;
        [SerializeField] private GoapSensorUpdateMode _updateMode = GoapSensorUpdateMode.EveryDecision;
        [SerializeField, Min(0.05f)] private float _interval = 0.5f;

        public string Name => string.IsNullOrWhiteSpace(_name) ? _kind.ToString() : _name;
        public GoapProfileSensorKind Kind => _kind;
        public GoapFact Fact => _fact;
        public string SourceId => _sourceId;
        public string TargetId => _targetId;
        public string RequiredTag => _requiredTag;
        public LayerMask LayerMask => _layerMask;
        public float Radius => Mathf.Max(0f, _radius);
        public GoapComparison Comparison => _comparison;
        public float Threshold => _threshold;
        public float Scale => _scale;
        public float Offset => _offset;
        public string ComponentType => _componentType;
        public string MemberName => _memberName;
        public GoapFactValueReference ConstantValue => _constantValue;
        public GoapSensorUpdateMode UpdateMode => _updateMode;
        public float Interval => Mathf.Max(0.05f, _interval);

        public GoapProfileSensorDefinition(
            string name,
            GoapProfileSensorKind kind,
            GoapFact fact,
            string sourceId = null,
            string targetId = null,
            float threshold = 1f,
            GoapComparison comparison = GoapComparison.GreaterOrEqual,
            GoapSensorUpdateMode updateMode = GoapSensorUpdateMode.EveryDecision,
            float interval = 0.5f)
        {
            _name = name;
            _kind = kind;
            _fact = fact;
            _sourceId = sourceId;
            _targetId = targetId;
            _requiredTag = string.Empty;
            _layerMask = ~0;
            _radius = 5f;
            _comparison = comparison;
            _threshold = threshold;
            _scale = 1f;
            _offset = 0f;
            _componentType = string.Empty;
            _memberName = string.Empty;
            _constantValue = default;
            _updateMode = updateMode;
            _interval = Mathf.Max(0.05f, interval);
        }

        public void ConfigureProximity(float radius, LayerMask layerMask, string requiredTag = null)
        {
            _radius = Mathf.Max(0f, radius);
            _layerMask = layerMask;
            _requiredTag = requiredTag;
        }

        public void ConfigureRange(float radius)
        {
            _radius = Mathf.Max(0f, radius);
        }

        public void ConfigureValueTransform(float scale, float offset)
        {
            _scale = scale;
            _offset = offset;
        }

        public void ConfigureProperty(string componentType, string memberName)
        {
            _componentType = componentType;
            _memberName = memberName;
        }

        public void ConfigureConstant(GoapFactValueReference value)
        {
            _constantValue = value;
        }
    }
}
