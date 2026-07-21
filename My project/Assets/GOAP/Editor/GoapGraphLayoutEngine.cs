using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    public static class GoapGraphLayoutEngine
    {
        private const float HorizontalSpacing = 170f;
        private const float VerticalSpacing = 54f;
        private const int OrderingPasses = 6;

        public static IReadOnlyDictionary<GoapDefinition, Vector2> Calculate(
            GoapDomain domain,
            IReadOnlyDictionary<GoapDefinition, Rect> nodeRects,
            IEnumerable<GoapDefinition> selection = null,
            Vector2? origin = null)
        {
            if (domain == null)
            {
                return new Dictionary<GoapDefinition, Vector2>();
            }

            var selectionSet = selection?.Where(item => item != null).ToHashSet();
            var definitions = domain.Facts.Cast<GoapDefinition>()
                .Concat(domain.Actions)
                .Concat(domain.Goals)
                .Where(item => item != null && (selectionSet == null || selectionSet.Contains(item)))
                .Distinct()
                .ToArray();
            if (definitions.Length == 0)
            {
                return new Dictionary<GoapDefinition, Vector2>();
            }

            var indices = definitions
                .Select((definition, index) => (definition, index))
                .ToDictionary(pair => pair.definition, pair => pair.index);
            var edges = BuildEdges(domain, indices);
            var components = new TarjanSearch(definitions.Length, edges).Run();
            var ranks = CalculateRanks(definitions, edges, components);
            var layers = BuildOrderedLayers(definitions, edges, ranks);
            return PositionLayers(
                definitions,
                nodeRects ?? new Dictionary<GoapDefinition, Rect>(),
                layers,
                origin ?? new Vector2(60f, 80f));
        }

        private static IReadOnlyList<LayoutEdge> BuildEdges(
            GoapDomain domain,
            IReadOnlyDictionary<GoapDefinition, int> indices)
        {
            var edges = new HashSet<LayoutEdge>();
            var desiredFacts = domain.Goals
                .Where(goal => goal != null)
                .SelectMany(goal => goal.DesiredState)
                .Where(condition => condition.Fact != null)
                .Select(condition => condition.Fact)
                .ToHashSet();

            foreach (var action in domain.Actions.Where(action => action != null && indices.ContainsKey(action)))
            {
                var preconditions = action.Preconditions
                    .Where(condition => condition.Fact != null)
                    .Select(condition => condition.Fact)
                    .ToHashSet();
                var effects = action.Effects
                    .Where(condition => condition.Fact != null)
                    .Select(condition => condition.Fact)
                    .ToHashSet();

                foreach (var fact in preconditions)
                {
                    var isDesiredStateTransition = effects.Contains(fact) && desiredFacts.Contains(fact);
                    if (!isDesiredStateTransition)
                    {
                        AddEdge(edges, indices, fact, action);
                    }
                }

                foreach (var fact in effects)
                {
                    var isNonGoalFeedback = preconditions.Contains(fact) && !desiredFacts.Contains(fact);
                    if (!isNonGoalFeedback)
                    {
                        AddEdge(edges, indices, action, fact);
                    }
                }
            }

            foreach (var goal in domain.Goals.Where(goal => goal != null && indices.ContainsKey(goal)))
            {
                foreach (var condition in goal.ActivationConditions.Concat(goal.DesiredState))
                {
                    AddEdge(edges, indices, condition.Fact, goal);
                }
            }

            return edges.ToArray();
        }

        private static void AddEdge(
            ISet<LayoutEdge> edges,
            IReadOnlyDictionary<GoapDefinition, int> indices,
            GoapDefinition from,
            GoapDefinition to)
        {
            if (from != null && to != null &&
                indices.TryGetValue(from, out var fromIndex) &&
                indices.TryGetValue(to, out var toIndex) &&
                fromIndex != toIndex)
            {
                edges.Add(new LayoutEdge(fromIndex, toIndex));
            }
        }

        private static int[] CalculateRanks(
            IReadOnlyList<GoapDefinition> definitions,
            IReadOnlyList<LayoutEdge> edges,
            IReadOnlyList<IReadOnlyList<int>> components)
        {
            var componentByNode = new int[definitions.Count];
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                foreach (var node in components[componentIndex])
                {
                    componentByNode[node] = componentIndex;
                }
            }

            var outgoing = Enumerable.Range(0, components.Count)
                .Select(_ => new HashSet<int>())
                .ToArray();
            var incomingCount = new int[components.Count];
            foreach (var edge in edges)
            {
                var from = componentByNode[edge.From];
                var to = componentByNode[edge.To];
                if (from != to && outgoing[from].Add(to))
                {
                    incomingCount[to]++;
                }
            }

            var componentRanks = components
                .Select(component => component.Max(node => BaselineRank(definitions[node])))
                .ToArray();
            var stableKeys = components.Select(component => component.Min()).ToArray();
            var available = Enumerable.Range(0, components.Count)
                .Where(component => incomingCount[component] == 0)
                .ToList();
            while (available.Count > 0)
            {
                available.Sort((left, right) => stableKeys[left].CompareTo(stableKeys[right]));
                var component = available[0];
                available.RemoveAt(0);
                foreach (var target in outgoing[component])
                {
                    componentRanks[target] = Math.Max(componentRanks[target], componentRanks[component] + 1);
                    incomingCount[target]--;
                    if (incomingCount[target] == 0)
                    {
                        available.Add(target);
                    }
                }
            }

            var distinctRanks = componentRanks.Distinct().OrderBy(rank => rank).ToArray();
            var compressedRanks = distinctRanks
                .Select((rank, index) => (rank, index))
                .ToDictionary(pair => pair.rank, pair => pair.index);
            var result = new int[definitions.Count];
            for (var node = 0; node < definitions.Count; node++)
            {
                result[node] = compressedRanks[componentRanks[componentByNode[node]]];
            }

            return result;
        }

        private static SortedDictionary<int, List<int>> BuildOrderedLayers(
            IReadOnlyList<GoapDefinition> definitions,
            IReadOnlyList<LayoutEdge> edges,
            IReadOnlyList<int> ranks)
        {
            var layers = new SortedDictionary<int, List<int>>();
            for (var node = 0; node < definitions.Count; node++)
            {
                if (!layers.TryGetValue(ranks[node], out var layer))
                {
                    layer = new List<int>();
                    layers.Add(ranks[node], layer);
                }

                layer.Add(node);
            }

            foreach (var layer in layers.Values)
            {
                layer.Sort((left, right) => CompareDefinitions(definitions[left], definitions[right]));
            }

            var incoming = Enumerable.Range(0, definitions.Count).Select(_ => new List<int>()).ToArray();
            var outgoing = Enumerable.Range(0, definitions.Count).Select(_ => new List<int>()).ToArray();
            foreach (var edge in edges)
            {
                outgoing[edge.From].Add(edge.To);
                incoming[edge.To].Add(edge.From);
            }

            var layerKeys = layers.Keys.ToArray();
            for (var pass = 0; pass < OrderingPasses; pass++)
            {
                for (var layerIndex = 1; layerIndex < layerKeys.Length; layerIndex++)
                {
                    SortByBarycenter(layers, layers[layerKeys[layerIndex]], incoming, ranks, true);
                }

                for (var layerIndex = layerKeys.Length - 2; layerIndex >= 0; layerIndex--)
                {
                    SortByBarycenter(layers, layers[layerKeys[layerIndex]], outgoing, ranks, false);
                }
            }

            return layers;
        }

        private static void SortByBarycenter(
            IReadOnlyDictionary<int, List<int>> layers,
            List<int> layer,
            IReadOnlyList<List<int>> neighbours,
            IReadOnlyList<int> ranks,
            bool useEarlierLayers)
        {
            var order = new Dictionary<int, int>();
            foreach (var candidateLayer in layers.Values)
            {
                for (var index = 0; index < candidateLayer.Count; index++)
                {
                    order[candidateLayer[index]] = index;
                }
            }

            var originalOrder = layer.Select((node, index) => (node, index))
                .ToDictionary(pair => pair.node, pair => pair.index);
            layer.Sort((left, right) =>
            {
                var leftBarycenter = GetBarycenter(left, neighbours, ranks, order, useEarlierLayers);
                var rightBarycenter = GetBarycenter(right, neighbours, ranks, order, useEarlierLayers);
                if (leftBarycenter.HasValue != rightBarycenter.HasValue)
                {
                    return leftBarycenter.HasValue ? -1 : 1;
                }

                if (leftBarycenter.HasValue)
                {
                    var comparison = leftBarycenter.Value.CompareTo(rightBarycenter.Value);
                    if (comparison != 0)
                    {
                        return comparison;
                    }
                }

                return originalOrder[left].CompareTo(originalOrder[right]);
            });
        }

        private static float? GetBarycenter(
            int node,
            IReadOnlyList<List<int>> neighbours,
            IReadOnlyList<int> ranks,
            IReadOnlyDictionary<int, int> order,
            bool useEarlierLayers)
        {
            var relevant = neighbours[node]
                .Where(neighbour => useEarlierLayers ? ranks[neighbour] < ranks[node] : ranks[neighbour] > ranks[node])
                .Select(neighbour => order[neighbour])
                .ToArray();
            return relevant.Length == 0 ? null : (float)relevant.Average();
        }

        private static IReadOnlyDictionary<GoapDefinition, Vector2> PositionLayers(
            IReadOnlyList<GoapDefinition> definitions,
            IReadOnlyDictionary<GoapDefinition, Rect> nodeRects,
            IReadOnlyDictionary<int, List<int>> layers,
            Vector2 origin)
        {
            var sizes = definitions.ToDictionary(
                definition => definition,
                definition => GetNodeSize(definition, nodeRects));
            var layerHeights = layers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Sum(node => sizes[definitions[node]].y) +
                        Math.Max(0, pair.Value.Count - 1) * VerticalSpacing);
            var maxHeight = layerHeights.Values.Max();
            var positions = new Dictionary<GoapDefinition, Vector2>();
            var x = origin.x;

            foreach (var pair in layers)
            {
                var layerWidth = pair.Value.Max(node => sizes[definitions[node]].x);
                var y = origin.y + (maxHeight - layerHeights[pair.Key]) * 0.5f;
                foreach (var node in pair.Value)
                {
                    var definition = definitions[node];
                    positions[definition] = new Vector2(x, y);
                    y += sizes[definition].y + VerticalSpacing;
                }

                x += layerWidth + HorizontalSpacing;
            }

            return positions;
        }

        private static Vector2 GetNodeSize(
            GoapDefinition definition,
            IReadOnlyDictionary<GoapDefinition, Rect> nodeRects)
        {
            var fallback = definition switch
            {
                GoapFact _ => new Vector2(245f, 105f),
                GoapGoalDefinition _ => new Vector2(280f, 190f),
                _ => new Vector2(270f, 180f)
            };
            if (!nodeRects.TryGetValue(definition, out var rect))
            {
                return fallback;
            }

            return new Vector2(
                Mathf.Max(fallback.x, rect.width),
                Mathf.Max(fallback.y, rect.height));
        }

        private static int BaselineRank(GoapDefinition definition)
        {
            return definition switch
            {
                GoapFact _ => 0,
                GoapActionDefinition _ => 1,
                GoapGoalDefinition _ => 3,
                _ => 0
            };
        }

        private static int CompareDefinitions(GoapDefinition left, GoapDefinition right)
        {
            var kindComparison = BaselineRank(left).CompareTo(BaselineRank(right));
            return kindComparison != 0
                ? kindComparison
                : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct LayoutEdge : IEquatable<LayoutEdge>
        {
            public readonly int From;
            public readonly int To;

            public LayoutEdge(int from, int to)
            {
                From = from;
                To = to;
            }

            public bool Equals(LayoutEdge other)
            {
                return From == other.From && To == other.To;
            }

            public override bool Equals(object obj)
            {
                return obj is LayoutEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(From, To);
            }
        }

        private sealed class TarjanSearch
        {
            private readonly IReadOnlyList<List<int>> _adjacency;
            private readonly int[] _indices;
            private readonly int[] _lowLinks;
            private readonly bool[] _onStack;
            private readonly Stack<int> _stack = new();
            private readonly List<IReadOnlyList<int>> _components = new();
            private int _nextIndex;

            public TarjanSearch(int nodeCount, IReadOnlyList<LayoutEdge> edges)
            {
                var adjacency = Enumerable.Range(0, nodeCount).Select(_ => new List<int>()).ToArray();
                foreach (var edge in edges)
                {
                    adjacency[edge.From].Add(edge.To);
                }

                _adjacency = adjacency;
                _indices = Enumerable.Repeat(-1, nodeCount).ToArray();
                _lowLinks = new int[nodeCount];
                _onStack = new bool[nodeCount];
            }

            public IReadOnlyList<IReadOnlyList<int>> Run()
            {
                for (var node = 0; node < _indices.Length; node++)
                {
                    if (_indices[node] < 0)
                    {
                        Visit(node);
                    }
                }

                return _components;
            }

            private void Visit(int node)
            {
                _indices[node] = _nextIndex;
                _lowLinks[node] = _nextIndex;
                _nextIndex++;
                _stack.Push(node);
                _onStack[node] = true;

                foreach (var target in _adjacency[node])
                {
                    if (_indices[target] < 0)
                    {
                        Visit(target);
                        _lowLinks[node] = Math.Min(_lowLinks[node], _lowLinks[target]);
                    }
                    else if (_onStack[target])
                    {
                        _lowLinks[node] = Math.Min(_lowLinks[node], _indices[target]);
                    }
                }

                if (_lowLinks[node] != _indices[node])
                {
                    return;
                }

                var component = new List<int>();
                int member;
                do
                {
                    member = _stack.Pop();
                    _onStack[member] = false;
                    component.Add(member);
                }
                while (member != node);

                _components.Add(component);
            }
        }
    }
}
