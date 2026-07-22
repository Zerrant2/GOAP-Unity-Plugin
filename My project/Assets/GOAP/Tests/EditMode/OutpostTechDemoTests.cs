using System.Linq;
using NUnit.Framework;
using Practice.GOAP.Editor;
using Practice.GOAP.TechDemo;
using UnityEditor;

namespace Practice.GOAP.Tests
{
    public sealed class OutpostTechDemoTests
    {
        [Test]
        public void GeneratedDomainHasNoValidationErrors()
        {
            var domain = LoadDomain();
            var errors = GoapDomainValidator.Validate(domain)
                .Where(issue => issue.Severity == GoapValidationSeverity.Error)
                .Select(issue => issue.Message)
                .ToArray();

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void LumberjackPlansHarvestBeforeDelivery()
        {
            var domain = LoadDomain();
            var profile = LoadProfile("Lumberjack");
            var state = domain.CreateDefaultState();
            state.SetValue(domain.FindFact("Need Wood"), GoapValue.From(true));
            state.SetValue(domain.FindFact("Wood Delivered"), GoapValue.From(false));
            state.SetValue(domain.FindFact("Tree Available"), GoapValue.From(true));
            state.SetValue(domain.FindFact("Carry Wood"), GoapValue.From(0));
            var goal = profile.Goals.Single(item => item.DisplayName == "Collect Wood");

            var result = new GoapPlanner().PlanCompiled(
                state, profile.Actions, goal, domain.Compile(), profile.PlannerSettings);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(
                result.Plan.Actions.Select(item => item.ExecutorId),
                Is.EqualTo(new[] { "outpost.harvest-wood", "outpost.deliver-wood" }));
        }

        [Test]
        public void BuilderCanGatherWoodBeforeRepairingEmptyCampStockpile()
        {
            var domain = LoadDomain();
            var profile = LoadProfile("Builder");
            var state = domain.CreateDefaultState();
            state.SetValue(domain.FindFact("Camp Damaged"), GoapValue.From(true));
            state.SetValue(domain.FindFact("Camp Repaired"), GoapValue.From(false));
            state.SetValue(domain.FindFact("Wood Stockpile"), GoapValue.From(0));
            state.SetValue(domain.FindFact("Tree Available"), GoapValue.From(true));
            state.SetValue(domain.FindFact("Carry Wood"), GoapValue.From(0));
            var goal = profile.Goals.Single(item => item.DisplayName == "Repair Camp");

            var result = new GoapPlanner().PlanCompiled(
                state, profile.Actions, goal, domain.Compile(), profile.PlannerSettings);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(
                result.Plan.Actions.Select(item => item.ExecutorId),
                Is.EqualTo(new[]
                {
                    "outpost.harvest-wood",
                    "outpost.deliver-wood",
                    "outpost.repair"
                }));
        }

        [Test]
        public void GuardEquipsWeaponBeforeAttacking()
        {
            var domain = LoadDomain();
            var profile = LoadProfile("Guard");
            var state = domain.CreateDefaultState();
            state.SetValue(domain.FindFact("Enemy Visible"), GoapValue.From(true));
            state.SetValue(domain.FindFact("Enemy Defeated"), GoapValue.From(false));
            state.SetValue(domain.FindFact("Has Weapon"), GoapValue.From(false));
            var goal = profile.Goals.Single(item => item.DisplayName == "Defend Outpost");

            var result = new GoapPlanner().PlanCompiled(
                state, profile.Actions, goal, domain.Compile(), profile.PlannerSettings);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(
                result.Plan.Actions.Select(item => item.ExecutorId),
                Is.EqualTo(new[] { "outpost.take-weapon", "outpost.attack" }));
        }

        [Test]
        public void StockpileRejectsOverspending()
        {
            var gameObject = new UnityEngine.GameObject("Stockpile Test");
            try
            {
                var stockpile = gameObject.AddComponent<OutpostStockpile>();
                stockpile.Configure(2, 1);

                Assert.That(stockpile.TryTake(OutpostResourceKind.Wood, 3), Is.False);
                Assert.That(stockpile.Wood, Is.EqualTo(2));
                Assert.That(stockpile.TryTake(OutpostResourceKind.Food, 1), Is.True);
                Assert.That(stockpile.Food, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static GoapDomain LoadDomain()
        {
            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(OutpostTechDemoBuilder.DomainPath);
            Assert.That(domain, Is.Not.Null, "Build the Outpost generated content first.");
            return domain;
        }

        private static GoapAgentProfile LoadProfile(string role)
        {
            var profile = AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(
                $"Assets/GOAP/TechDemo/Generated/{role} Profile.asset");
            Assert.That(profile, Is.Not.Null);
            return profile;
        }
    }
}
