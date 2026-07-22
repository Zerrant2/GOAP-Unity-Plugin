using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public sealed class GoapGoalSelector
    {
        public GoapGoalDefinition Select(
            GoapWorldState state,
            IEnumerable<GoapGoalDefinition> goals)
        {
            return SelectDetailed(state, goals).SelectedGoal;
        }

        public GoapGoalSelectionResult SelectDetailed(
            GoapWorldState state,
            IEnumerable<GoapGoalDefinition> goals,
            GoapGoalDefinition currentGoal = null,
            float switchThreshold = 0f,
            Func<GoapGoalDefinition, IEnumerable<GoapGoalScoreTerm>> externalScore = null,
            Func<GoapGoalDefinition, float> cooldownRemaining = null)
        {
            if (state == null || goals == null)
            {
                return new GoapGoalSelectionResult(null, null, Array.Empty<GoapGoalEvaluation>());
            }

            var evaluations = goals
                .Where(goal => goal != null)
                .Select(goal => Evaluate(state, goal, externalScore, cooldownRemaining))
                .ToArray();
            var selected = evaluations
                .Where(item => item.Eligible)
                .OrderByDescending(item => item.FinalScore)
                .ThenBy(item => item.Goal.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Goal.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            var current = evaluations.FirstOrDefault(item => item.Goal == currentGoal);
            if (selected != null && current != null && current.Eligible && selected.Goal != currentGoal &&
                selected.FinalScore <= current.FinalScore + Math.Max(0f, switchThreshold))
            {
                selected = current;
            }

            return new GoapGoalSelectionResult(selected?.Goal, selected, evaluations);
        }

        public GoapGoalEvaluation Evaluate(
            GoapWorldState state,
            GoapGoalDefinition goal,
            Func<GoapGoalDefinition, IEnumerable<GoapGoalScoreTerm>> externalScore = null,
            Func<GoapGoalDefinition, float> cooldownRemaining = null)
        {
            if (state == null || goal == null)
            {
                return null;
            }

            var terms = new List<GoapGoalScoreTerm>();
            foreach (var modifier in goal.ScoreModifiers.Where(item => item != null && item.IsValid))
            {
                terms.Add(new GoapGoalScoreTerm(modifier.BuildLabel(state), modifier.Evaluate(state)));
            }

            if (externalScore != null)
            {
                var externalTerms = externalScore(goal);
                if (externalTerms != null)
                {
                    terms.AddRange(externalTerms);
                }
            }

            return new GoapGoalEvaluation(
                goal,
                goal.Priority,
                terms,
                goal.DesiredState.Count > 0 && state.Satisfies(goal.ActivationConditions),
                goal.DesiredState.Count == 0 || state.Satisfies(goal.DesiredState),
                cooldownRemaining?.Invoke(goal) ?? 0f);
        }
    }
}
