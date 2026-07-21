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
            Assert.That(GoapProfileCoverageAnalyzer.Analyze(profile).IsComplete, Is.True);
            Assert.That(
                GoapEditorDomainValidator.Validate(domain)
                    .Where(issue => issue.Severity == GoapValidationSeverity.Warning),
                Is.Empty);
        }

        [Test]
        public void EditorValidationKeepsWarningForMissingProfileValueProvider()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddBasicNeedsPreset(domain);
            var profile = GoapContentCreationService.CreateProfile(
                domain,
                "Unconfigured Needs Profile",
                preset.Actions,
                preset.Goals,
                null,
                null);

            var warnings = GoapEditorDomainValidator.Validate(domain)
                .Where(issue => issue.Severity == GoapValidationSeverity.Warning)
                .ToArray();

            Assert.That(warnings, Is.Not.Empty);
            Assert.That(warnings.All(issue => issue.FixKind == GoapValidationFixKind.OpenSensor), Is.True);
            Assert.That(warnings.Any(issue => issue.Message.Contains(profile.name)), Is.True);
        }

        [Test]
        public void EditorValidationTreatsUnassignedActionCoverageAsInformation()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddBasicNeedsPreset(domain);
            GoapContentCreationService.CreateProfile(domain, "Basic Needs Profile", preset);
            var unusedAction = GoapContentCreationService.CreateAction(
                domain,
                "Inspect Food",
                1f,
                "inspect-food",
                new[] { new GoapCondition(domain.FindFact("Food Available"), true) },
                new[] { new GoapCondition(domain.FindFact("Has Food"), true) },
                new[] { GoapActionStep.Wait(0.1f) });

            var issues = GoapEditorDomainValidator.Validate(domain)
                .Where(issue => issue.Source == unusedAction)
                .ToArray();

            Assert.That(issues.Any(issue => issue.Severity == GoapValidationSeverity.Warning), Is.False);
            Assert.That(issues.Any(issue => issue.Severity == GoapValidationSeverity.Info), Is.True);
        }

        [Test]
        public void ProfileProvidersCanBeReplacedRemovedAndAnalyzed()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddBasicNeedsPreset(domain);
            var profile = GoapContentCreationService.CreateProfile(domain, "Needs Profile", preset);
            var foodAvailable = domain.FindFact("Food Available");
            var hungry = domain.FindFact("Is Hungry");

            GoapContentCreationService.RemoveProfileSensor(profile, foodAvailable);
            var missingSensor = GoapProfileCoverageAnalyzer.Analyze(profile);
            Assert.That(missingSensor.IsComplete, Is.False);
            Assert.That(missingSensor.MissingFacts, Does.Contain(foodAvailable));

            var replacement = new GoapProfileSensorDefinition(
                "Food Constant",
                GoapProfileSensorKind.Constant,
                foodAvailable);
            replacement.ConfigureConstant(new GoapFactValueReference(foodAvailable, true));
            GoapContentCreationService.SetProfileSensor(profile, replacement);
            Assert.That(profile.Sensors.Count(sensor => sensor.Fact == foodAvailable), Is.EqualTo(1));
            Assert.That(GoapProfileCoverageAnalyzer.Analyze(profile).IsComplete, Is.True);

            GoapContentCreationService.SetProfileInitialFact(
                profile,
                new GoapFactValueReference(hungry, false));
            Assert.That(GoapProfileCoverageAnalyzer.Analyze(profile).MissingFacts, Does.Contain(hungry));
            GoapContentCreationService.SetProfileInitialFact(
                profile,
                new GoapFactValueReference(hungry, true));
            Assert.That(GoapProfileCoverageAnalyzer.Analyze(profile).IsComplete, Is.True);
        }

        [Test]
        public void SyncProfileSceneAgentsAddsRequiredSources()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddResourceGatheringPreset(
                domain,
                "Stone",
                "Rock",
                "Stone",
                1,
                30);
            var profile = GoapContentCreationService.CreateProfile(domain, "Miner Profile", preset);
            var agent = new GameObject("Miner");
            SceneManager.MoveGameObjectToScene(agent, _testScene);

            try
            {
                GoapContentCreationService.SetupAgent(agent, profile, false, false);
                Assert.That(agent.GetComponent<GoapInventory>(), Is.Null);

                var updated = GoapContentCreationService.SyncProfileSceneAgents(profile);

                Assert.That(updated, Is.EqualTo(1));
                Assert.That(agent.GetComponent<GoapInventory>(), Is.Not.Null);
                Assert.That(GoapProfileCoverageAnalyzer.Analyze(profile).RequiresInventory, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(agent);
            }
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

        [Test]
        public void ProfileComposerFindsBasicNeedsDependencies()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddBasicNeedsPreset(domain);

            var analysis = GoapProfileComposer.Analyze(domain, preset.Goals);

            Assert.That(analysis.CanCreateProfile, Is.True);
            Assert.That(analysis.Goals.Count, Is.EqualTo(2));
            Assert.That(analysis.Actions.Count, Is.EqualTo(3));
            Assert.That(analysis.InitialFacts.Count, Is.EqualTo(2));
            Assert.That(analysis.Sensors.Count, Is.EqualTo(2));
            Assert.That(analysis.UnreachableConditions, Is.Empty);
            Assert.That(analysis.UnresolvedFacts, Is.Empty);
            Assert.That(analysis.RequiresInventory, Is.False);
            Assert.That(analysis.Sensors.Select(sensor => sensor.Kind),
                Is.All.EqualTo(GoapProfileSensorKind.SmartObject));
        }

        [Test]
        public void ProfileComposerInfersResourceSensorsAndCreatesProfile()
        {
            var domain = CreateDomain();
            var preset = GoapContentCreationService.AddResourceGatheringPreset(
                domain,
                "Stone",
                "Rock",
                "Stone",
                2,
                35);
            var analysis = GoapProfileComposer.Analyze(domain, preset.Goals);

            var profile = GoapContentCreationService.CreateProfile(
                domain,
                "Composed Miner Profile",
                analysis.Actions,
                analysis.Goals,
                analysis.InitialFacts,
                analysis.Sensors);

            Assert.That(analysis.CanCreateProfile, Is.True);
            Assert.That(analysis.Actions.Count, Is.EqualTo(1));
            Assert.That(analysis.Sensors.Count, Is.EqualTo(2));
            Assert.That(analysis.Sensors.Select(sensor => sensor.Kind),
                Is.EquivalentTo(new[]
                {
                    GoapProfileSensorKind.SmartObject,
                    GoapProfileSensorKind.Inventory
                }));
            Assert.That(analysis.RequiresInventory, Is.True);
            Assert.That(analysis.UnresolvedFacts, Is.Empty);
            Assert.That(profile.Actions, Has.Count.EqualTo(1));
            Assert.That(profile.Goals, Has.Count.EqualTo(1));
            Assert.That(profile.Sensors, Has.Count.EqualTo(2));
        }

        [Test]
        public void ProfileComposerRejectsGoalWithoutProducer()
        {
            var domain = CreateDomain();
            var rescued = GoapContentCreationService.CreateFact(
                domain,
                "Civilian Rescued",
                GoapFactType.Boolean,
                GoapValue.From(false));
            var goal = GoapContentCreationService.CreateGoal(
                domain,
                "Rescue Civilian",
                50,
                null,
                new[] { new GoapCondition(rescued, true) });

            var analysis = GoapProfileComposer.Analyze(domain, new[] { goal });

            Assert.That(analysis.CanCreateProfile, Is.False);
            Assert.That(analysis.Actions, Is.Empty);
            Assert.That(analysis.UnreachableConditions.Count, Is.EqualTo(1));
            Assert.That(analysis.UnreachableConditions.Single().Fact, Is.SameAs(rescued));
        }

        [Test]
        public void ProfileComposerRejectsCausalActionCycleWithoutStartingFact()
        {
            var domain = CreateDomain();
            var alpha = GoapContentCreationService.CreateFact(
                domain,
                "Alpha Ready",
                GoapFactType.Boolean,
                GoapValue.From(false));
            var beta = GoapContentCreationService.CreateFact(
                domain,
                "Beta Ready",
                GoapFactType.Boolean,
                GoapValue.From(false));
            GoapContentCreationService.CreateAction(
                domain,
                "Prepare Alpha",
                1f,
                "prepare-alpha",
                new[] { new GoapCondition(beta, true) },
                new[] { new GoapCondition(alpha, true) },
                new[] { GoapActionStep.Wait(0.1f) });
            GoapContentCreationService.CreateAction(
                domain,
                "Prepare Beta",
                1f,
                "prepare-beta",
                new[] { new GoapCondition(alpha, true) },
                new[] { new GoapCondition(beta, true) },
                new[] { GoapActionStep.Wait(0.1f) });
            var goal = GoapContentCreationService.CreateGoal(
                domain,
                "Reach Alpha",
                10,
                null,
                new[] { new GoapCondition(alpha, true) });

            var analysis = GoapProfileComposer.Analyze(domain, new[] { goal });

            Assert.That(analysis.Actions.Count, Is.EqualTo(2));
            Assert.That(analysis.CanCreateProfile, Is.False);
            Assert.That(analysis.UnreachableConditions.Count, Is.EqualTo(1));
            Assert.That(analysis.UnreachableConditions.Single().Fact, Is.SameAs(alpha));
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
