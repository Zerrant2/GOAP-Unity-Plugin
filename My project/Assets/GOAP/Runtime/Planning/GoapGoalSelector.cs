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
            if (state == null || goals == null)
            {
                return null;
            }

            return goals
                .Where(goal => goal != null)
                .Where(goal => goal.DesiredState.Count > 0)
                .Where(goal => state.Satisfies(goal.ActivationConditions))
                .Where(goal => !state.Satisfies(goal.DesiredState))
                .OrderByDescending(goal => goal.Priority)
                .ThenBy(goal => goal.DisplayName, StringComparer.Ordinal)
                .ThenBy(goal => goal.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
