using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Practice.GOAP.Editor;
using UnityEditor;
using UnityEngine;
using System.Threading;

namespace Practice.GOAP.Tests
{
    public sealed class GoapPlannerTests
    {
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
            {
                Object.DestroyImmediate(item);
            }

            _created.Clear();
        }

        [Test]
        public void BuildsMultiStepPlanFromPreconditions()
        {
            var hungry = Fact("Hungry", true);
            var hasFood = Fact("Has Food", false);
            var getFood = Action("Get Food", 1f, "get-food",
                Conditions((hasFood, false)),
                Conditions((hasFood, true)));
            var eat = Action("Eat", 1f, "eat",
                Conditions((hasFood, true), (hungry, true)),
                Conditions((hasFood, false), (hungry, false)));
            var goal = Goal("Be Fed", 10, Conditions((hungry, true)), Conditions((hungry, false)));
            var state = new GoapWorldState();

            var settings = GoapPlannerSettings.Default;
            settings.MaxPlanningMilliseconds = 100f;
            var result = new GoapPlanner().Plan(state, new[] { eat, getFood }, goal, settings);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Plan.Actions, Is.EqualTo(new[] { getFood, eat }));
            Assert.That(result.Plan.TotalCost, Is.EqualTo(2f));
        }

        [Test]
        public void ChoosesCheapestPlanInsteadOfFewestActions()
        {
            var ready = Fact("Ready", false);
            var prepared = Fact("Prepared", false);
            var expensive = Action("Buy Solution", 10f, "buy", null, Conditions((ready, true)));
            var prepare = Action("Prepare", 2f, "prepare", null, Conditions((prepared, true)));
            var finish = Action("Finish", 2f, "finish", Conditions((prepared, true)), Conditions((ready, true)));
            var goal = Goal("Ready", 1, null, Conditions((ready, true)));

            var result = new GoapPlanner().Plan(
                new GoapWorldState(),
                new[] { expensive, prepare, finish },
                goal);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Plan.Actions, Is.EqualTo(new[] { prepare, finish }));
            Assert.That(result.Plan.TotalCost, Is.EqualTo(4f));
        }

        [Test]
        public void ReportsGoalWithNoProducer()
        {
            var safe = Fact("Safe", false);
            var goal = Goal("Be Safe", 1, null, Conditions((safe, true)));

            var result = new GoapPlanner().Plan(
                new GoapWorldState(),
                System.Array.Empty<GoapActionDefinition>(),
                goal);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(GoapPlanFailure.GoalHasNoProducer));
        }

        [Test]
        public void ReplanningUsesLatestWorldState()
        {
            var hungry = Fact("Hungry", true);
            var hasFood = Fact("Has Food", false);
            var getFood = Action("Get Food", 1f, "get-food", null, Conditions((hasFood, true)));
            var eat = Action("Eat", 1f, "eat",
                Conditions((hasFood, true)),
                Conditions((hungry, false)));
            var goal = Goal("Be Fed", 1, null, Conditions((hungry, false)));
            var state = new GoapWorldState();
            state.Set(hasFood, true);

            var result = new GoapPlanner().Plan(state, new[] { getFood, eat }, goal);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Plan.Actions, Is.EqualTo(new[] { eat }));
        }

        [Test]
        public void NumericFactsSupportComparisonAndAddEffects()
        {
            var wood = ScriptableObject.CreateInstance<GoapFact>();
            wood.ConfigureInteger("Wood", 0);
            _created.Add(wood);
            var gather = Action(
                "Gather Wood",
                1f,
                "gather",
                new[] { new GoapCondition(wood, 2, GoapComparison.Less) },
                new[] { new GoapCondition(wood, 1, GoapComparison.Equal, GoapEffectOperation.Add) });
            var goal = Goal(
                "Stock Wood",
                10,
                null,
                new[] { new GoapCondition(wood, 2, GoapComparison.GreaterOrEqual) });

            var result = new GoapPlanner().Plan(new GoapWorldState(), new[] { gather }, goal);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Plan.Actions, Has.Count.EqualTo(2));
            Assert.That(result.Plan.Actions.All(action => action == gather), Is.True);
        }

        [Test]
        public void SmartObjectReservationPreventsTwoAgentsFromClaimingOneTarget()
        {
            var targetObject = new GameObject("Tree");
            var firstAgentObject = new GameObject("First Agent");
            var secondAgentObject = new GameObject("Second Agent");
            try
            {
                var target = targetObject.AddComponent<GoapSmartObject>();
                target.Configure("Wood", false, 1);
                var first = firstAgentObject.AddComponent<GoapAgent>();
                var second = secondAgentObject.AddComponent<GoapAgent>();

                Assert.That(target.TryReserve(first), Is.True);
                Assert.That(target.TryReserve(second), Is.False);
                target.Release(first);
                Assert.That(target.TryReserve(second), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(firstAgentObject);
                Object.DestroyImmediate(secondAgentObject);
            }
        }

        [Test]
        public void SmartObjectQueuePromotesNextAgentAfterRelease()
        {
            var targetObject = new GameObject("Bed");
            var firstAgentObject = new GameObject("First Agent");
            var secondAgentObject = new GameObject("Second Agent");
            try
            {
                var target = targetObject.AddComponent<GoapSmartObject>();
                target.Configure("Bed", false, 1);
                var first = firstAgentObject.AddComponent<GoapAgent>();
                var second = secondAgentObject.AddComponent<GoapAgent>();

                Assert.That(target.RequestReservation(first), Is.True);
                Assert.That(target.RequestReservation(second), Is.False);
                Assert.That(target.IsQueued(second), Is.True);
                target.Release(first);
                Assert.That(target.IsReservedBy(second), Is.True);
                Assert.That(target.IsQueued(second), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(firstAgentObject);
                Object.DestroyImmediate(secondAgentObject);
            }
        }

        [Test]
        public void EnumFactsParticipateInPlanning()
        {
            var mood = ScriptableObject.CreateInstance<GoapFact>();
            mood.ConfigureEnum("Mood", new[] { "Calm", "Alert", "Afraid" }, 0);
            _created.Add(mood);
            var react = Action(
                "React",
                1f,
                "react",
                new[] { new GoapCondition(mood, 0) },
                new[] { new GoapCondition(mood, 1) });
            var goal = Goal("Become Alert", 10, null, new[] { new GoapCondition(mood, 1) });

            var result = new GoapPlanner().Plan(new GoapWorldState(), new[] { react }, goal);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Plan.Actions, Is.EqualTo(new[] { react }));
        }

        [Test]
        public void PlanningHonoursCancellationToken()
        {
            var ready = Fact("Ready", false);
            var finish = Action("Finish", 1f, "finish", null, Conditions((ready, true)));
            var goal = Goal("Ready", 1, null, Conditions((ready, true)));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = new GoapPlanner().Plan(
                new GoapWorldState(),
                new[] { finish },
                goal,
                cancellationToken: cancellation.Token);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(GoapPlanFailure.Cancelled));
        }

        [Test]
        public void GoalSelectorUsesPriorityAndActivationConditions()
        {
            var hungry = Fact("Hungry", false);
            var threatened = Fact("Threatened", true);
            var safe = Fact("Safe", false);
            var fed = Fact("Fed", false);
            var eatGoal = Goal("Eat", 10, Conditions((hungry, true)), Conditions((fed, true)));
            var surviveGoal = Goal("Survive", 100, Conditions((threatened, true)), Conditions((safe, true)));
            var state = new GoapWorldState();

            var selected = new GoapGoalSelector().Select(state, new[] { eatGoal, surviveGoal });

            Assert.That(selected, Is.SameAs(surviveGoal));
        }

        [Test]
        public void ActionDiagnosticReportsExpectedAndActualTypedValues()
        {
            var wood = ScriptableObject.CreateInstance<GoapFact>();
            wood.ConfigureInteger("Wood", 0);
            _created.Add(wood);
            var gather = Action(
                "Gather Wood",
                1f,
                "gather",
                new[] { new GoapCondition(wood, 3, GoapComparison.GreaterOrEqual) },
                null);
            var state = new GoapWorldState();
            state.Set(wood, 1);

            var diagnostic = GoapDiagnosticUtility.EvaluateAction(gather, state, true);

            Assert.That(diagnostic.Executable, Is.False);
            Assert.That(diagnostic.HasExecutor, Is.True);
            Assert.That(diagnostic.Preconditions.Count, Is.EqualTo(1));
            Assert.That(diagnostic.Preconditions[0].Requirement, Is.EqualTo("Wood >= 3"));
            Assert.That(diagnostic.Preconditions[0].Actual, Is.EqualTo("1"));
            StringAssert.Contains("actual 1", diagnostic.Reason);
        }

        [Test]
        public void GeneratedDemoDomainPassesValidation()
        {
            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(
                "Assets/GOAP/Demo/Generated/GOAP Demo Domain.asset");

            Assert.That(domain, Is.Not.Null);
            Assert.That(domain.FindFact("Is Hungry"), Is.Not.Null);
            Assert.That(domain.FindFact("Is Tired"), Is.Not.Null);
            Assert.That(domain.FindAction("take-food"), Is.Not.Null);
            Assert.That(domain.FindAction("eat"), Is.Not.Null);
            Assert.That(domain.FindAction("rest"), Is.Not.Null);
            Assert.That(domain.Goals.Any(goal => goal != null && goal.DisplayName == "Satisfy Hunger"), Is.True);
            Assert.That(domain.Goals.Any(goal => goal != null && goal.DisplayName == "Recover Energy"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(
                "Assets/GOAP/Demo/Generated/Worker Profile.asset"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(
                "Assets/GOAP/Demo/Generated/Guard Profile.asset"), Is.Not.Null);

            var errors = GoapDomainValidator.Validate(domain)
                .Where(issue => issue.Severity == GoapValidationSeverity.Error)
                .Select(issue => issue.Message)
                .ToArray();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));

            var editorWarnings = GoapEditorDomainValidator.Validate(domain)
                .Where(issue => issue.Severity == GoapValidationSeverity.Warning)
                .Select(issue => issue.Message)
                .ToArray();
            Assert.That(editorWarnings, Is.Empty, string.Join("\n", editorWarnings));
        }

        private GoapFact Fact(string name, bool defaultValue)
        {
            var fact = ScriptableObject.CreateInstance<GoapFact>();
            fact.Configure(name, defaultValue);
            _created.Add(fact);
            return fact;
        }

        private GoapActionDefinition Action(
            string name,
            float cost,
            string executor,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects)
        {
            var action = ScriptableObject.CreateInstance<GoapActionDefinition>();
            action.Configure(name, cost, executor, preconditions, effects);
            _created.Add(action);
            return action;
        }

        private GoapGoalDefinition Goal(
            string name,
            int priority,
            IEnumerable<GoapCondition> activation,
            IEnumerable<GoapCondition> desired)
        {
            var goal = ScriptableObject.CreateInstance<GoapGoalDefinition>();
            goal.Configure(name, priority, activation, desired);
            _created.Add(goal);
            return goal;
        }

        private static GoapCondition[] Conditions(params (GoapFact fact, bool value)[] values)
        {
            var result = new GoapCondition[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                result[index] = new GoapCondition(values[index].fact, values[index].value);
            }

            return result;
        }
    }
}
