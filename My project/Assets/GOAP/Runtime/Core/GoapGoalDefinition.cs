using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Goal", fileName = "New Goal")]
    public sealed class GoapGoalDefinition : GoapDefinition
    {
        [SerializeField] private int _priority = 1;
        [SerializeField, Min(0f)] private float _cooldownSeconds;
        [SerializeField] private List<GoapGoalScoreModifier> _scoreModifiers = new();
        [SerializeField] private List<GoapCondition> _activationConditions = new();
        [SerializeField] private List<GoapCondition> _desiredState = new();

        public int Priority => _priority;
        public float CooldownSeconds => Mathf.Max(0f, _cooldownSeconds);
        public IReadOnlyList<GoapGoalScoreModifier> ScoreModifiers =>
            _scoreModifiers != null ? _scoreModifiers : Array.Empty<GoapGoalScoreModifier>();
        public IReadOnlyList<GoapCondition> ActivationConditions => _activationConditions;
        public IReadOnlyList<GoapCondition> DesiredState => _desiredState;

        public void Configure(
            string displayName,
            int priority,
            IEnumerable<GoapCondition> activationConditions,
            IEnumerable<GoapCondition> desiredState,
            string description = "")
        {
            SetIdentity(displayName, description);
            _priority = priority;
            _activationConditions = activationConditions == null
                ? new List<GoapCondition>()
                : new List<GoapCondition>(activationConditions);
            _desiredState = desiredState == null
                ? new List<GoapCondition>()
                : new List<GoapCondition>(desiredState);
        }

        public void ConfigureSelection(
            float cooldownSeconds,
            IEnumerable<GoapGoalScoreModifier> scoreModifiers = null)
        {
            _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            _scoreModifiers = scoreModifiers == null
                ? new List<GoapGoalScoreModifier>()
                : new List<GoapGoalScoreModifier>(scoreModifiers.Where(item => item != null));
        }

        public bool AddScoreModifier(GoapGoalScoreModifier modifier)
        {
            if (modifier == null || !modifier.IsValid)
            {
                return false;
            }

            _scoreModifiers ??= new List<GoapGoalScoreModifier>();
            _scoreModifiers.Add(modifier);
            return true;
        }

        public bool RemoveScoreModifier(GoapGoalScoreModifier modifier)
        {
            return modifier != null && _scoreModifiers != null && _scoreModifiers.Remove(modifier);
        }

        public bool AddActivationCondition(GoapCondition condition)
        {
            if (!condition.IsValid || _activationConditions.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _activationConditions.Add(condition);
            return true;
        }

        public bool AddDesiredCondition(GoapCondition condition)
        {
            if (!condition.IsValid || _desiredState.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _desiredState.Add(condition);
            return true;
        }

        public bool RemoveActivationCondition(GoapFact fact)
        {
            return _activationConditions.RemoveAll(item => item.Fact == fact) > 0;
        }

        public bool RemoveDesiredCondition(GoapFact fact)
        {
            return _desiredState.RemoveAll(item => item.Fact == fact) > 0;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            _cooldownSeconds = Mathf.Max(0f, _cooldownSeconds);
            _scoreModifiers ??= new List<GoapGoalScoreModifier>();
            _activationConditions ??= new List<GoapCondition>();
            _desiredState ??= new List<GoapCondition>();
        }
    }
}
