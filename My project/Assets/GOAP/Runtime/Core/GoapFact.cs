using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Fact", fileName = "New Fact")]
    public sealed class GoapFact : GoapDefinition
    {
        [SerializeField] private GoapFactType _valueType;
        [SerializeField] private bool _defaultValue;
        [SerializeField] private int _defaultInteger;
        [SerializeField] private float _defaultFloat;
        [SerializeField] private List<string> _enumOptions = new() { "None" };
        [SerializeField] private int _defaultEnumIndex;

        public GoapFactType ValueType => _valueType;
        public bool DefaultValue => _defaultValue;
        public GoapValue DefaultTypedValue => _valueType switch
        {
            GoapFactType.Integer => GoapValue.From(_defaultInteger),
            GoapFactType.Float => GoapValue.From(_defaultFloat),
            GoapFactType.Enum => GoapValue.FromEnum(NormalizeEnumIndex(_defaultEnumIndex)),
            _ => GoapValue.From(_defaultValue)
        };
        public IReadOnlyList<string> EnumOptions => _enumOptions;

        public void Configure(string displayName, bool defaultValue, string description = "")
        {
            SetIdentity(displayName, description);
            _valueType = GoapFactType.Boolean;
            _defaultValue = defaultValue;
        }

        public void ConfigureInteger(string displayName, int defaultValue, string description = "")
        {
            SetIdentity(displayName, description);
            _valueType = GoapFactType.Integer;
            _defaultInteger = defaultValue;
        }

        public void ConfigureFloat(string displayName, float defaultValue, string description = "")
        {
            SetIdentity(displayName, description);
            _valueType = GoapFactType.Float;
            _defaultFloat = defaultValue;
        }

        public void ConfigureEnum(
            string displayName,
            IEnumerable<string> options,
            int defaultIndex = 0,
            string description = "")
        {
            SetIdentity(displayName, description);
            _valueType = GoapFactType.Enum;
            _enumOptions = options == null ? new List<string>() : new List<string>(options);
            EnsureEnumOptions();
            _defaultEnumIndex = NormalizeEnumIndex(defaultIndex);
        }

        public int NormalizeEnumIndex(int index)
        {
            EnsureEnumOptions();
            return Mathf.Clamp(index, 0, _enumOptions.Count - 1);
        }

        public string FormatValue(GoapValue value)
        {
            if (_valueType != GoapFactType.Enum)
            {
                return value.ConvertTo(_valueType).ToString();
            }

            var index = NormalizeEnumIndex(value.Integer);
            return _enumOptions[index];
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureEnumOptions();
            _defaultEnumIndex = NormalizeEnumIndex(_defaultEnumIndex);
        }

        private void EnsureEnumOptions()
        {
            _enumOptions ??= new List<string>();
            if (_enumOptions.Count == 0)
            {
                _enumOptions.Add("None");
            }

            for (var index = 0; index < _enumOptions.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(_enumOptions[index]))
                {
                    _enumOptions[index] = $"Value {index}";
                }
            }
        }
    }
}
