using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public readonly struct GoapGoalScoreTerm
    {
        public string Label { get; }
        public float Value { get; }

        public GoapGoalScoreTerm(string label, float value)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Modifier" : label;
            Value = value;
        }
    }

    public sealed class GoapGoalEvaluation
    {
        private readonly IReadOnlyList<GoapGoalScoreTerm> _scoreTerms;

        public GoapGoalDefinition Goal { get; }
        public float BaseScore { get; }
        public float ModifierScore { get; }
        public float FinalScore => BaseScore + ModifierScore;
        public bool Active { get; }
        public bool Satisfied { get; }
        public float CooldownRemaining { get; }
        public bool OnCooldown => CooldownRemaining > 0f;
        public bool Eligible => Goal != null && Active && !Satisfied && !OnCooldown;
        public IReadOnlyList<GoapGoalScoreTerm> ScoreTerms => _scoreTerms;

        public GoapGoalEvaluation(
            GoapGoalDefinition goal,
            float baseScore,
            IEnumerable<GoapGoalScoreTerm> scoreTerms,
            bool active,
            bool satisfied,
            float cooldownRemaining)
        {
            Goal = goal;
            BaseScore = baseScore;
            _scoreTerms = (scoreTerms ?? Array.Empty<GoapGoalScoreTerm>()).ToArray();
            ModifierScore = _scoreTerms.Sum(term => term.Value);
            Active = active;
            Satisfied = satisfied;
            CooldownRemaining = Math.Max(0f, cooldownRemaining);
        }
    }

    public sealed class GoapGoalSelectionResult
    {
        private readonly IReadOnlyList<GoapGoalEvaluation> _evaluations;

        public GoapGoalDefinition SelectedGoal { get; }
        public GoapGoalEvaluation SelectedEvaluation { get; }
        public IReadOnlyList<GoapGoalEvaluation> Evaluations => _evaluations;

        public GoapGoalSelectionResult(
            GoapGoalDefinition selectedGoal,
            GoapGoalEvaluation selectedEvaluation,
            IEnumerable<GoapGoalEvaluation> evaluations)
        {
            SelectedGoal = selectedGoal;
            SelectedEvaluation = selectedEvaluation;
            _evaluations = (evaluations ?? Array.Empty<GoapGoalEvaluation>()).ToArray();
        }

        public GoapGoalEvaluation Find(GoapGoalDefinition goal)
        {
            return goal == null ? null : _evaluations.FirstOrDefault(item => item.Goal == goal);
        }
    }
}
