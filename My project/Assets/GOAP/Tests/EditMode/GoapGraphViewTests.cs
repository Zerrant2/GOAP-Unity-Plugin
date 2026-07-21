using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Practice.GOAP.Editor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Practice.GOAP.Tests
{
    public sealed class GoapGraphViewTests
    {
        private const string DemoScenePath = "Assets/GOAP/Demo/Scenes/GOAP Demo.unity";

        [Test]
        public void DemoSceneScriptReferencesResolve()
        {
            var absolutePath = Path.GetFullPath(DemoScenePath);
            var sceneYaml = File.ReadAllText(absolutePath);
            var scriptReferences = Regex.Matches(
                sceneYaml,
                @"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}");

            Assert.That(scriptReferences.Count, Is.GreaterThan(0));
            foreach (Match reference in scriptReferences)
            {
                var guid = reference.Groups[1].Value;
                Assert.That(
                    AssetDatabase.GUIDToAssetPath(guid),
                    Is.Not.Empty,
                    $"Demo scene contains a missing script reference with GUID '{guid}'.");
            }
        }

        [Test]
        public void DemoDomainBuildsExpectedVisualGraph()
        {
            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(
                "Assets/GOAP/Demo/Generated/GOAP Demo Domain.asset");
            var graph = new GoapGraphView();

            graph.Rebuild(domain, _ => { });

            var expectedNodeCount = domain.Facts.Count(fact => fact != null) +
                                    domain.Actions.Count(action => action != null) +
                                    domain.Goals.Count(goal => goal != null);
            Assert.That(graph.nodes.Count(), Is.EqualTo(expectedNodeCount));
            Assert.That(graph.edges.Count(), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void DemoDomainCausalLayoutUsesLayersWithoutNodeOverlap()
        {
            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(
                "Assets/GOAP/Demo/Generated/GOAP Demo Domain.asset");
            var definitions = domain.Facts.Cast<GoapDefinition>()
                .Concat(domain.Actions)
                .Concat(domain.Goals)
                .Where(definition => definition != null)
                .ToArray();
            var rects = definitions.ToDictionary(
                definition => definition,
                definition => new Rect(Vector2.zero, GetTestNodeSize(definition)));

            var positions = GoapGraphLayoutEngine.Calculate(domain, rects);
            var arrangedRects = definitions
                .Select(definition => new Rect(positions[definition], rects[definition].size))
                .ToArray();

            Assert.That(positions, Has.Count.EqualTo(definitions.Length));
            Assert.That(positions.Values.Select(position => position.x).Distinct().Count(), Is.GreaterThanOrEqualTo(4));
            for (var first = 0; first < arrangedRects.Length; first++)
            {
                for (var second = first + 1; second < arrangedRects.Length; second++)
                {
                    Assert.That(
                        arrangedRects[first].Overlaps(arrangedRects[second]),
                        Is.False,
                        $"Layout overlaps '{definitions[first].DisplayName}' and '{definitions[second].DisplayName}'.");
                }
            }
        }

        [Test]
        public void ReadabilityControlsCollapseNodesAndFilterConnectionTypes()
        {
            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(
                "Assets/GOAP/Demo/Generated/GOAP Demo Domain.asset");
            var graph = new GoapGraphView();
            graph.Rebuild(domain, _ => { });

            Assert.That(graph.nodes.Cast<Node>().All(node => !node.expanded), Is.True);
            Assert.That(graph.edges.Count(), Is.GreaterThan(0));

            graph.SetPreconditionsVisible(false);
            graph.SetEffectsVisible(false);
            graph.SetGoalLinksVisible(false);
            Assert.That(
                graph.edges.Cast<Edge>().All(edge => edge.style.display.value == DisplayStyle.None),
                Is.True);

            graph.SetPreconditionsVisible(true);
            graph.SetEffectsVisible(true);
            graph.SetGoalLinksVisible(true);
            Assert.That(
                graph.edges.Cast<Edge>().All(edge => edge.style.display.value == DisplayStyle.Flex),
                Is.True);

            graph.SetDetailsVisible(true);
            Assert.That(graph.nodes.Cast<Node>().All(node => node.expanded), Is.True);

            graph.FocusDefinition(domain.Goals.First(goal => goal != null));
            Assert.That(
                graph.nodes.Cast<Node>().Any(node => node.style.opacity.value < 0.2f),
                Is.True,
                "Focus mode did not dim unrelated graph branches.");
            graph.SetFocusMode(false);
            Assert.That(
                graph.nodes.Cast<Node>().All(node => node.style.opacity.value >= 0.99f),
                Is.True,
                "Disabling focus mode did not restore all graph nodes.");
        }

        [Test]
        public void CausalLayoutOrdersStateTransitionFromLeftToRight()
        {
            var toolAvailable = ScriptableObject.CreateInstance<GoapFact>();
            var needsWood = ScriptableObject.CreateInstance<GoapFact>();
            var gather = ScriptableObject.CreateInstance<GoapActionDefinition>();
            var collect = ScriptableObject.CreateInstance<GoapGoalDefinition>();
            var domain = ScriptableObject.CreateInstance<GoapDomain>();

            try
            {
                toolAvailable.Configure("Tool Available", true);
                needsWood.Configure("Needs Wood", true);
                gather.Configure(
                    "Gather Wood",
                    1f,
                    string.Empty,
                    new[]
                    {
                        new GoapCondition(toolAvailable, true),
                        new GoapCondition(needsWood, true)
                    },
                    new[] { new GoapCondition(needsWood, false) });
                collect.Configure(
                    "Collect Wood",
                    10,
                    null,
                    new[] { new GoapCondition(needsWood, false) });
                domain.AddFact(toolAvailable);
                domain.AddFact(needsWood);
                domain.AddAction(gather);
                domain.AddGoal(collect);

                var rects = new Dictionary<GoapDefinition, Rect>
                {
                    [toolAvailable] = new Rect(0f, 0f, 245f, 105f),
                    [needsWood] = new Rect(0f, 0f, 245f, 105f),
                    [gather] = new Rect(0f, 0f, 270f, 180f),
                    [collect] = new Rect(0f, 0f, 280f, 190f)
                };
                var positions = GoapGraphLayoutEngine.Calculate(domain, rects);

                Assert.That(positions[toolAvailable].x, Is.LessThan(positions[gather].x));
                Assert.That(positions[gather].x, Is.LessThan(positions[needsWood].x));
                Assert.That(positions[needsWood].x, Is.LessThan(positions[collect].x));
            }
            finally
            {
                Object.DestroyImmediate(domain);
                Object.DestroyImmediate(collect);
                Object.DestroyImmediate(gather);
                Object.DestroyImmediate(needsWood);
                Object.DestroyImmediate(toolAvailable);
            }
        }

        private static Vector2 GetTestNodeSize(GoapDefinition definition)
        {
            return definition switch
            {
                GoapFact _ => new Vector2(245f, 105f),
                GoapGoalDefinition _ => new Vector2(280f, 190f),
                _ => new Vector2(270f, 180f)
            };
        }
    }
}
