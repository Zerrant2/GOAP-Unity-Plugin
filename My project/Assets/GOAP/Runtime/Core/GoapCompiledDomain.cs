using System;
using System.Collections.Generic;
using System.Linq;

namespace Practice.GOAP
{
    public sealed class GoapCompiledDomain
    {
        private readonly GoapFact[] _facts;
        private readonly GoapActionDefinition[] _actions;
        private readonly GoapGoalDefinition[] _goals;
        private readonly Dictionary<GoapFact, int> _factIndices;
        private readonly GoapPlannerStateLayout _plannerLayout;
        private readonly Dictionary<GoapActionDefinition, GoapCompiledAction> _compiledActions;
        private readonly Dictionary<GoapActionDefinition, int> _actionSignatures;
        private readonly Dictionary<GoapGoalDefinition, GoapCompiledCondition[]> _compiledGoals;
        private readonly Dictionary<GoapGoalDefinition, int> _goalSignatures;

        public IReadOnlyList<GoapFact> Facts => _facts;
        public IReadOnlyList<GoapActionDefinition> Actions => _actions;
        public IReadOnlyList<GoapGoalDefinition> Goals => _goals;
        public IReadOnlyDictionary<GoapFact, int> FactIndices => _factIndices;

        internal GoapCompiledDomain(GoapDomain source)
        {
            _facts = source.Facts.Where(item => item != null).ToArray();
            _actions = source.Actions.Where(item => item != null).ToArray();
            _goals = source.Goals.Where(item => item != null).ToArray();
            _factIndices = new Dictionary<GoapFact, int>(_facts.Length);
            for (var index = 0; index < _facts.Length; index++)
            {
                _factIndices[_facts[index]] = index;
            }

            _plannerLayout = new GoapPlannerStateLayout(_facts);
            _compiledActions = new Dictionary<GoapActionDefinition, GoapCompiledAction>(_actions.Length);
            _actionSignatures = new Dictionary<GoapActionDefinition, int>(_actions.Length);
            foreach (var action in _actions)
            {
                _compiledActions[action] = new GoapCompiledAction(action, _plannerLayout);
                _actionSignatures[action] = GetActionSignature(action);
            }

            _compiledGoals = new Dictionary<GoapGoalDefinition, GoapCompiledCondition[]>(_goals.Length);
            _goalSignatures = new Dictionary<GoapGoalDefinition, int>(_goals.Length);
            foreach (var goal in _goals)
            {
                _compiledGoals[goal] = _plannerLayout.Compile(goal.DesiredState);
                _goalSignatures[goal] = GetConditionsSignature(goal.DesiredState);
            }
        }

        public bool TryGetFactIndex(GoapFact fact, out int index)
        {
            return _factIndices.TryGetValue(fact, out index);
        }

        public GoapWorldState CreateDefaultState()
        {
            return new GoapWorldState(_facts, _factIndices);
        }

        internal GoapPlannerState CapturePlannerState(GoapWorldState source)
        {
            return _plannerLayout.Capture(source);
        }

        internal GoapCompiledAction GetCompiledAction(GoapActionDefinition action)
        {
            var signature = GetActionSignature(action);
            if (!_compiledActions.TryGetValue(action, out var compiled) ||
                !_actionSignatures.TryGetValue(action, out var cachedSignature) ||
                cachedSignature != signature)
            {
                compiled = new GoapCompiledAction(action, _plannerLayout);
                _compiledActions[action] = compiled;
                _actionSignatures[action] = signature;
            }

            return compiled;
        }

        internal GoapCompiledCondition[] GetCompiledGoal(GoapGoalDefinition goal)
        {
            var signature = GetConditionsSignature(goal.DesiredState);
            if (!_compiledGoals.TryGetValue(goal, out var compiled) ||
                !_goalSignatures.TryGetValue(goal, out var cachedSignature) ||
                cachedSignature != signature)
            {
                compiled = _plannerLayout.Compile(goal.DesiredState);
                _compiledGoals[goal] = compiled;
                _goalSignatures[goal] = signature;
            }

            return compiled;
        }

        private static int GetActionSignature(GoapActionDefinition action)
        {
            return HashCode.Combine(
                GetConditionsSignature(action.Preconditions),
                GetConditionsSignature(action.Effects));
        }

        private static int GetConditionsSignature(IReadOnlyList<GoapCondition> conditions)
        {
            var hash = 17;
            for (var index = 0; index < conditions.Count; index++)
            {
                hash = unchecked(hash * 31 + conditions[index].GetHashCode());
            }

            return unchecked(hash * 31 + conditions.Count);
        }
    }
}
