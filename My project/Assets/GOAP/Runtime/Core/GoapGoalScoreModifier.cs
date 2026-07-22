using System;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public sealed class GoapGoalScoreModifier
    {
        [SerializeField] private GoapFact _fact;
        [SerializeField] private float _inputMinimum;
        [SerializeField] private float _inputMaximum = 1f;
        [SerializeField] private float _scoreAtMinimum;
        [SerializeField] private float _scoreAtMaximum = 10f;
        [SerializeField] private bool _clampInput = true;

        public GoapFact Fact => _fact;
        public float InputMinimum => _inputMinimum;
        public float InputMaximum => _inputMaximum;
        public float ScoreAtMinimum => _scoreAtMinimum;
        public float ScoreAtMaximum => _scoreAtMaximum;
        public bool ClampInput => _clampInput;
        public bool IsValid => _fact != null && !Mathf.Approximately(_inputMinimum, _inputMaximum);

        public GoapGoalScoreModifier()
        {
        }

        public GoapGoalScoreModifier(
            GoapFact fact,
            float inputMinimum,
            float inputMaximum,
            float scoreAtMinimum,
            float scoreAtMaximum,
            bool clampInput = true)
        {
            Configure(fact, inputMinimum, inputMaximum, scoreAtMinimum, scoreAtMaximum, clampInput);
        }

        public void Configure(
            GoapFact fact,
            float inputMinimum,
            float inputMaximum,
            float scoreAtMinimum,
            float scoreAtMaximum,
            bool clampInput = true)
        {
            _fact = fact;
            _inputMinimum = inputMinimum;
            _inputMaximum = inputMaximum;
            _scoreAtMinimum = scoreAtMinimum;
            _scoreAtMaximum = scoreAtMaximum;
            _clampInput = clampInput;
        }

        public float Evaluate(GoapWorldState state)
        {
            if (!IsValid || state == null)
            {
                return 0f;
            }

            var value = GetNumericValue(state.GetValue(_fact));
            var amount = (value - _inputMinimum) / (_inputMaximum - _inputMinimum);
            if (_clampInput)
            {
                amount = Mathf.Clamp01(amount);
            }

            return Mathf.LerpUnclamped(_scoreAtMinimum, _scoreAtMaximum, amount);
        }

        public string BuildLabel(GoapWorldState state)
        {
            if (_fact == null)
            {
                return "Missing fact";
            }

            var value = state == null ? _fact.DefaultTypedValue : state.GetValue(_fact);
            return $"{_fact.DisplayName} = {_fact.FormatValue(value)}";
        }

        private static float GetNumericValue(GoapValue value)
        {
            return value.Type == GoapFactType.Boolean
                ? value.Boolean ? 1f : 0f
                : value.Float;
        }
    }
}
