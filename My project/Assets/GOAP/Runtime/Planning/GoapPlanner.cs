using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Unity.Profiling;

namespace Practice.GOAP
{
    public sealed class GoapPlanner
    {
        private const float CostEpsilon = 0.0001f;
        private static readonly ProfilerMarker PlanMarker = new("GOAP.Plan");

        public GoapPlanResult Plan(
            GoapWorldState initialState,
            IEnumerable<GoapActionDefinition> availableActions,
            GoapGoalDefinition goal,
            GoapPlannerSettings? settings = null,
            CancellationToken cancellationToken = default)
        {
            using var marker = PlanMarker.Auto();
            var stopwatch = Stopwatch.StartNew();
            if (initialState == null || availableActions == null || goal == null || goal.DesiredState.Count == 0)
            {
                return GoapPlanResult.Failed(GoapPlanFailure.InvalidInput, "Planner input is incomplete.");
            }

            var options = (settings ?? GoapPlannerSettings.Default).Sanitized();
            var actions = availableActions
                .Where(IsUsable)
                .OrderBy(action => action.DisplayName, StringComparer.Ordinal)
                .ThenBy(action => action.Id, StringComparer.Ordinal)
                .ToArray();

            var relevantFacts = CollectRelevantFacts(actions, goal);
            if (initialState.Satisfies(goal.DesiredState))
            {
                return GoapPlanResult.Succeeded(new GoapPlan(
                    new List<GoapActionDefinition>(), 0f, 0, stopwatch.Elapsed.TotalMilliseconds));
            }

            if (!EveryGoalFactHasProducer(initialState, actions, goal))
            {
                return GoapPlanResult.Failed(
                    GoapPlanFailure.GoalHasNoProducer,
                    $"No action can establish every desired fact for goal '{goal.DisplayName}'.");
            }

            var sequence = 0L;
            var initialNode = new SearchNode(
                initialState.Clone(),
                null,
                null,
                0f,
                EstimateRemainingCost(initialState, actions, goal),
                0,
                sequence++);

            var open = new MinHeap<SearchNode>();
            open.Push(initialNode);

            var bestKnownCost = new Dictionary<GoapStateKey, float>
            {
                [initialState.BuildKey(relevantFacts)] = 0f
            };

            var expandedStates = 0;
            while (open.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return GoapPlanResult.Failed(
                        GoapPlanFailure.Cancelled,
                        "Planning was cancelled.");
                }

                if (stopwatch.Elapsed.TotalMilliseconds >= options.MaxPlanningMilliseconds)
                {
                    return GoapPlanResult.Failed(
                        GoapPlanFailure.TimeLimitReached,
                        $"Search exceeded the {options.MaxPlanningMilliseconds:0.##} ms planning budget.");
                }

                var current = open.Pop();
                var currentKey = current.State.BuildKey(relevantFacts);
                if (bestKnownCost.TryGetValue(currentKey, out var knownCost) && current.Cost > knownCost + CostEpsilon)
                {
                    continue;
                }

                if (current.State.Satisfies(goal.DesiredState))
                {
                    return GoapPlanResult.Succeeded(BuildPlan(
                        current, expandedStates, stopwatch.Elapsed.TotalMilliseconds));
                }

                if (expandedStates >= options.MaxExpandedStates)
                {
                    return GoapPlanResult.Failed(
                        GoapPlanFailure.SearchLimitReached,
                        $"Search stopped after {expandedStates} expanded states.");
                }

                expandedStates++;
                if (current.Depth >= options.MaxPlanDepth)
                {
                    continue;
                }

                foreach (var action in actions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return GoapPlanResult.Failed(
                            GoapPlanFailure.Cancelled,
                            "Planning was cancelled.");
                    }

                    if (!current.State.Satisfies(action.Preconditions))
                    {
                        continue;
                    }

                    var nextState = current.State.Clone();
                    if (!nextState.Apply(action.Effects))
                    {
                        continue;
                    }

                    var nextCost = current.Cost + action.Cost;
                    var nextKey = nextState.BuildKey(relevantFacts);
                    if (bestKnownCost.TryGetValue(nextKey, out var bestCost) && bestCost <= nextCost + CostEpsilon)
                    {
                        continue;
                    }

                    bestKnownCost[nextKey] = nextCost;
                    open.Push(new SearchNode(
                        nextState,
                        current,
                        action,
                        nextCost,
                        EstimateRemainingCost(nextState, actions, goal),
                        current.Depth + 1,
                        sequence++));
                }
            }

            return GoapPlanResult.Failed(
                GoapPlanFailure.NoPlanFound,
                $"No valid plan was found for goal '{goal.DisplayName}'.");
        }

        private static bool IsUsable(GoapActionDefinition action)
        {
            return action != null && action.Effects.Count > 0;
        }

        private static List<GoapFact> CollectRelevantFacts(
            IReadOnlyList<GoapActionDefinition> actions,
            GoapGoalDefinition goal)
        {
            var facts = new HashSet<GoapFact>();
            AddFacts(facts, goal.DesiredState);

            foreach (var action in actions)
            {
                AddFacts(facts, action.Preconditions);
                AddFacts(facts, action.Effects);
            }

            return facts
                .Where(fact => fact != null)
                .OrderBy(fact => fact.Id, StringComparer.Ordinal)
                .ThenBy(fact => fact.GetInstanceID())
                .ToList();
        }

        private static void AddFacts(HashSet<GoapFact> destination, IReadOnlyList<GoapCondition> conditions)
        {
            for (var index = 0; index < conditions.Count; index++)
            {
                if (conditions[index].Fact != null)
                {
                    destination.Add(conditions[index].Fact);
                }
            }
        }

        private static bool EveryGoalFactHasProducer(
            GoapWorldState state,
            IReadOnlyList<GoapActionDefinition> actions,
            GoapGoalDefinition goal)
        {
            foreach (var desired in goal.DesiredState)
            {
                if (!desired.IsValid)
                {
                    return false;
                }

                if (desired.Matches(state.GetValue(desired.Fact)))
                {
                    continue;
                }

                var found = actions.Any(action => action.Effects.Any(effect => effect.CanEstablish(desired)));
                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        // The maximum cheapest direct producer is an admissible lower bound: it ignores all prerequisites.
        private static float EstimateRemainingCost(
            GoapWorldState state,
            IReadOnlyList<GoapActionDefinition> actions,
            GoapGoalDefinition goal)
        {
            var estimate = 0f;
            foreach (var desired in goal.DesiredState)
            {
                if (!desired.IsValid || desired.Matches(state.GetValue(desired.Fact)))
                {
                    continue;
                }

                var cheapestProducer = float.PositiveInfinity;
                foreach (var action in actions)
                {
                    if (action.Effects.Any(effect => effect.CanEstablish(desired)))
                    {
                        cheapestProducer = Math.Min(cheapestProducer, action.Cost);
                    }
                }

                if (!float.IsPositiveInfinity(cheapestProducer))
                {
                    estimate = Math.Max(estimate, cheapestProducer);
                }
            }

            return estimate;
        }

        private static GoapPlan BuildPlan(
            SearchNode goalNode,
            int expandedStates,
            double planningMilliseconds)
        {
            var actions = new List<GoapActionDefinition>();
            var current = goalNode;
            while (current.Action != null)
            {
                actions.Add(current.Action);
                current = current.Parent;
            }

            actions.Reverse();
            return new GoapPlan(actions, goalNode.Cost, expandedStates, planningMilliseconds);
        }

        private sealed class SearchNode : IComparable<SearchNode>
        {
            public GoapWorldState State { get; }
            public SearchNode Parent { get; }
            public GoapActionDefinition Action { get; }
            public float Cost { get; }
            public float Heuristic { get; }
            public int Depth { get; }
            public long Sequence { get; }

            private float Score => Cost + Heuristic;

            public SearchNode(
                GoapWorldState state,
                SearchNode parent,
                GoapActionDefinition action,
                float cost,
                float heuristic,
                int depth,
                long sequence)
            {
                State = state;
                Parent = parent;
                Action = action;
                Cost = cost;
                Heuristic = heuristic;
                Depth = depth;
                Sequence = sequence;
            }

            public int CompareTo(SearchNode other)
            {
                var scoreComparison = Score.CompareTo(other.Score);
                if (scoreComparison != 0)
                {
                    return scoreComparison;
                }

                var heuristicComparison = Heuristic.CompareTo(other.Heuristic);
                if (heuristicComparison != 0)
                {
                    return heuristicComparison;
                }

                return Sequence.CompareTo(other.Sequence);
            }
        }
    }
}
