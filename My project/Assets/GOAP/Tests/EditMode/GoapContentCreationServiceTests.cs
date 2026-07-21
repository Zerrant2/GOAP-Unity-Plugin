using System;
using System.Linq;
using NUnit.Framework;
using Practice.GOAP.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Practice.GOAP.Tests
{
    public sealed class GoapContentCreationServiceTests
    {
        private string _temporaryFolder;
        private Scene _testScene;

        [SetUp]
        public void SetUp()
        {
            _testScene = EditorSceneManager.NewPreviewScene();
            var folderName = $"ContentWizard_{Guid.NewGuid():N}";
            AssetDatabase.CreateFolder("Assets/GOAP/Tests", folderName);
            _temporaryFolder = $"Assets/GOAP/Tests/{folderName}";
        }

        [TearDown]
        public void TearDown()
        {
            if (_testScene.IsValid() && _testScene.isLoaded)
            {
                EditorSceneManager.ClosePreviewScene(_testScene);
            }

            if (!string.IsNullOrWhiteSpace(_temporaryFolder))
            {
                AssetDatabase.DeleteAsset(_temporaryFolder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void BasicNeedsPresetCreatesConnectedContentAndConfiguredProfile()
        {
            var domain = CreateDomain();

            var result = GoapContentCreationService.AddBasicNeedsPreset(domain);
            var profile = GoapContentCreationService.CreateProfile(domain, "Basic Needs Profile", result);

            Assert.That(result.CreatedDefinitionCount, Is.EqualTo(10));
            Assert.That(domain.Facts.Count, Is.EqualTo(5));
            Assert.That(domain.Actions.Count, Is.EqualTo(3));
            Assert.That(domain.Goals.Count, Is.EqualTo(2));
            Assert.That(domain.FindAction("take-food").ExecutionSteps, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(domain.FindAction("eat").UsesBuiltInExecutor, Is.True);
            Assert.That(profile.Domain, Is.SameAs(domain));
            Assert.That(profile.Actions, Has.Count.EqualTo(3));
            Assert.That(profile.Goals, Has.Count.EqualTo(2));
            Assert.That(profile.InitialFacts, Has.Count.EqualTo(2));
            Assert.That(profile.Sensors, Has.Count.EqualTo(2));
            Assert.That(profile.InitialFacts.All(value => value.Value.Boolean), Is.True);
        }

        [Test]
        public void ResourcePresetIsIdempotentAndCreatesInventoryAgent()
        {
            var domain = CreateDomain();
            GameObject agent = null;

            try
            {
                var first = GoapContentCreationService.AddResourceGatheringPreset(
                    domain,
                    "Stone",
                    "Rock",
                    "Stone",
                    2,
                    35);
                var second = GoapContentCreationService.AddResourceGatheringPreset(
                    domain,
                    "Stone",
                    "Rock",
                    "Stone",
                    2,
                    35);
                var profile = GoapContentCreationService.CreateProfile(domain, "Miner Profile", first);
                agent = new GameObject("Miner");
                SceneManager.MoveGameObjectToScene(agent, _testScene);
                GoapContentCreationService.SetupAgent(
                    agent,
                    profile,
                    first.RequiresInventory,
                    true);

                Assert.That(first.CreatedDefinitionCount, Is.EqualTo(4));
                Assert.That(second.CreatedDefinitionCount, Is.Zero);
                Assert.That(domain.Facts, Has.Count.EqualTo(2));
                Assert.That(domain.Actions, Has.Count.EqualTo(1));
                Assert.That(domain.Goals, Has.Count.EqualTo(1));
                Assert.That(profile.Sensors, Has.Count.EqualTo(2));
                Assert.That(agent.GetComponent<GoapAgentAuthoring>().Profile, Is.SameAs(profile));
                Assert.That(agent.GetComponent<GoapAgent>(), Is.Not.Null);
                Assert.That(agent.GetComponent<GoapBuiltInActionBehaviour>(), Is.Not.Null);
                Assert.That(agent.GetComponent<GoapProfileSensorBehaviour>(), Is.Not.Null);
                Assert.That(agent.GetComponent<GoapInventory>(), Is.Not.Null);
                Assert.That(agent.GetComponent<GoapStatSource>(), Is.Not.Null);
            }
            finally
            {
                if (agent != null)
                {
                    UnityEngine.Object.DestroyImmediate(agent);
                }
            }
        }

        [Test]
        public void SetupSmartObjectUsesRequestedCategoryAndCapacity()
        {
            var gameObject = new GameObject("Workbench");
            SceneManager.MoveGameObjectToScene(gameObject, _testScene);
            try
            {
                var smartObject = GoapContentCreationService.SetupSmartObject(
                    gameObject,
                    "Crafting",
                    false,
                    3);

                Assert.That(smartObject.Category, Is.EqualTo("Crafting"));
                Assert.That(smartObject.Capacity, Is.EqualTo(3));
                Assert.That(smartObject.Available, Is.True);
                Assert.That(gameObject.GetComponents<GoapSmartObject>(), Has.Length.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CustomBuildersCreateTypedActionAndReachableGoal()
        {
            var domain = CreateDomain();
            var available = GoapContentCreationService.CreateFact(
                domain,
                "Ore Available",
                GoapFactType.Boolean,
                GoapValue.From(false));
            var count = GoapContentCreationService.CreateFact(
                domain,
                "Ore Count",
                GoapFactType.Integer,
                GoapValue.From(0));
            var action = GoapContentCreationService.CreateAction(
                domain,
                "Mine Ore",
                1.5f,
                "mine-ore",
                new[] { new GoapCondition(available, true) },
                new[]
                {
                    new GoapCondition(
                        count,
                        1,
                        GoapComparison.Equal,
                        GoapEffectOperation.Add)
                },
                new[] { GoapActionStep.Wait(0.1f) });
            var desired = new GoapCondition(count, 3, GoapComparison.GreaterOrEqual);
            var goal = GoapContentCreationService.CreateGoal(
                domain,
                "Collect Ore",
                30,
                null,
                new[] { desired });
            var move = GoapActionStep.MoveToNamedTarget("Mine", 1.25f, 4f, true);

            Assert.That(domain.Facts, Has.Count.EqualTo(2));
            Assert.That(domain.Actions, Has.Count.EqualTo(1));
            Assert.That(domain.Goals, Has.Count.EqualTo(1));
            Assert.That(action.Effects.Single().CanEstablish(desired), Is.True);
            Assert.That(goal.DesiredState.Single().Fact, Is.SameAs(count));
            Assert.That(move.TargetId, Is.EqualTo("Mine"));
            Assert.That(move.UseNavMesh, Is.True);
            Assert.Throws<InvalidOperationException>(() => GoapContentCreationService.CreateAction(
                domain,
                "Duplicate Mine",
                1f,
                "mine-ore",
                null,
                new[] { new GoapCondition(available, false) },
                new[] { GoapActionStep.Wait(0.1f) }));
        }

        private GoapDomain CreateDomain()
        {
            var domain = ScriptableObject.CreateInstance<GoapDomain>();
            domain.name = "Content Wizard Test Domain";
            AssetDatabase.CreateAsset(domain, $"{_temporaryFolder}/Domain.asset");
            return domain;
        }
    }
}
