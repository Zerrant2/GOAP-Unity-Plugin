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
        private static readonly ProfilerMarker CompileMarker = new("GOAP.Plan.CompileState");
        private static readonly ProfilerMarker SearchMarker = new("GOAP.Plan.Search");

        public GoapPlanResult Plan(
            GoapWorldState initialState,
            IEnumerable<GoapActionDefinition> availableActions,
            GoapGoalDefinition goal,
            GoapPlannerSettings? settings = null,
            CancellationToken cancellationToken = default)
        {
            return PlanInternal(
                initialState,
                availableActions,
                goal,
                null,
                settings,
                cancellationToken);
        }

        public GoapPlanResult PlanCompiled(
            GoapWorldState initialState,
            IEnumerable<GoapActionDefinition> availableActions,
            GoapGoalDefinition goal,
            GoapCompiledDomain compiledDomain,
            GoapPlannerSettings? settings = null,
            CancellationToken cancellationToken = default)
        {
            return PlanInternal(
                initialState,
                availableActions,
                goal,
                compiledDomain,
                settings,
                cancellationToken);
        }

        private GoapPlanResult PlanInternal(
            GoapWorldState initialState,
            IEnumerable<GoapActionDefinition> availableActions,
            GoapGoalDefinition goal,
            GoapCompiledDomain compiledDomain,
            GoapPlannerSettings? settings,
            CancellationToken cancellationToken)
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

            GoapPlannerState initialPlannerState;
            GoapCompiledAction[] compiledActions;
            GoapCompiledCondition[] compiledGoal;
            float[] cheapestProducerCosts;
            using (CompileMarker.Auto())
            {
                if (compiledDomain != null)
                {
                    initialPlannerState = compiledDomain.CapturePlannerState(initialState);
                    compiledActions = actions.Select(compiledDomain.GetCompiledAction).ToArray();
                    compiledGoal = compiledDomain.GetCompiledGoal(goal);
                }
                else
                {
                    var relevantFacts = CollectRelevantFacts(actions, goal);
                    var layout = new GoapPlannerStateLayout(relevantFacts);
                    initialPlannerState = layout.Capture(initialState);
                    compiledActions = actions
                        .Select(action => new GoapCompiledAction(action, layout))
                        .ToArray();
                    compiledGoal = layout.Compile(goal.DesiredState);
                }

                cheapestProducerCosts = CollectCheapestProducerCosts(actions, goal);
            }

            if (initialPlannerState.Satisfies(compiledGoal))
            {
                return GoapPlanResult.Succeeded(new GoapPlan(
                    new List<GoapActionDefinition>(), 0f, 0, stopwatch.Elapsed.TotalMilliseconds));
            }

            if (!EveryGoalFactHasProducer(initialPlannerState, compiledGoal, cheapestProducerCosts))
            {
                return GoapPlanResult.Failed(
                    GoapPlanFailure.GoalHasNoProducer,
                    $"No action can establish every desired fact for goal '{goal.DisplayName}'.");
            }

            var sequence = 0L;
            var initialNode = new SearchNode(
                initialPlannerState,
                null,
                null,
                0f,
                EstimateRemainingCost(initialPlannerState, compiledGoal, cheapestProducerCosts),
                0,
                sequence++);

            var open = new MinHeap<SearchNode>();
            open.Push(initialNode);

            var bestKnownCost = new Dictionary<GoapPlannerState, float>
            {
                [initialPlannerState] = 0f
            };

            var expandedStates = 0;
            using var searchMarker = SearchMarker.Auto();
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
                if (bestKnownCost.TryGetValue(current.State, out var knownCost) &&
                    current.Cost > knownCost + CostEpsilon)
                {
                    continue;
                }

                if (current.State.Satisfies(compiledGoal))
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

                foreach (var action in compiledActions)
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

                    var nextState = current.State.Apply(action.Effects);
                    if (nextState == null)
                    {
                        continue;
                    }

                    var nextCost = current.Cost + action.Definition.Cost;
                    if (bestKnownCost.TryGetValue(nextState, out var bestCost) &&
                        bestCost <= nextCost + CostEpsilon)
                    {
                        continue;
                    }

                    bestKnownCost[nextState] = nextCost;
                    open.Push(new SearchNode(
                        nextState,
                        current,
                        action.Definition,
                        nextCost,
                        EstimateRemainingCost(nextState, compiledGoal, cheapestProducerCosts),
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

        private static float[] CollectCheapestProducerCosts(
            IReadOnlyList<GoapActionDefinition> actions,
            GoapGoalDefinition goal)
        {
            var costs = new float[goal.DesiredState.Count];
            for (var desiredIndex = 0; desiredIndex < goal.DesiredState.Count; desiredIndex++)
            {
                var desired = goal.DesiredState[desiredIndex];
                costs[desiredIndex] = float.PositiveInfinity;
                if (!desired.IsValid)
                {
                    continue;
                }

                foreach (var action in actions)
                {
                    if (action.Effects.Any(effect => effect.CanEstablish(desired)))
                    {
                        costs[desiredIndex] = Math.Min(costs[desiredIndex], action.Cost);
                    }
                }
            }

            return costs;
        }

        private static bool EveryGoalFactHasProducer(
            GoapPlannerState state,
            IReadOnlyList<GoapCompiledCondition> desiredState,
            IReadOnlyList<float> cheapestProducerCosts)
        {
            for (var index = 0; index < desiredState.Count; index++)
            {
                var desired = desiredState[index];
                if (!desired.IsValid)
                {
                    return false;
                }

                if (state.GetValue(desired.Slot).Matches(desired.Comparison, desired.ExpectedValue))
                {
                    continue;
                }

                if (float.IsPositiveInfinity(cheapestProducerCosts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        // The maximum cheapest direct producer is an admissible lower bound: it ignores all prerequisites.
        private static float EstimateRemainingCost(
            GoapPlannerState state,
            IReadOnlyList<GoapCompiledCondition> desiredState,
            IReadOnlyList<float> cheapestProducerCosts)
        {
            var estimate = 0f;
            for (var index = 0; index < desiredState.Count; index++)
            {
                var desired = desiredState[index];
                if (!desired.IsValid || state.GetValue(desired.Slot).Matches(
                        desired.Comparison,
                        desired.ExpectedValue))
                {
                    continue;
                }

                var cheapestProducer = cheapestProducerCosts[index];
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
            public GoapPlannerState State { get; }
            public SearchNode Parent { get; }
            public GoapActionDefinition Action { get; }
            public float Cost { get; }
            public float Heuristic { get; }
            public int Depth { get; }
            public long Sequence { get; }

            private float Score => Cost + Heuristic;

            public SearchNode(
                GoapPlannerState state,
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
