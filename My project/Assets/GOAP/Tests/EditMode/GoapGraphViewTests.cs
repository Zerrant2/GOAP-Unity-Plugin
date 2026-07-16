using System.Linq;
using NUnit.Framework;
using Practice.GOAP.Editor;
using UnityEditor;

namespace Practice.GOAP.Tests
{
    public sealed class GoapGraphViewTests
    {
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
    }
}
