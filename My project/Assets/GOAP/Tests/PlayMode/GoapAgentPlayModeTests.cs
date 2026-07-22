using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Practice.GOAP.Demo;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Practice.GOAP.Tests
{
    public sealed class GoapAgentPlayModeTests
    {
        private readonly List<Object> _created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GoapPlanningScheduler.RestoreDefaults();
            foreach (var item in _created)
            {
                Object.Destroy(item);
            }

            _created.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AgentPlansExecutesAndCompletesGoal()
        {
            var needsHelp = Create<GoapFact>();
            needsHelp.Configure("Needs Help", true);

            var help = Create<GoapActionDefinition>();
            help.Configure(
                "Help",
                1f,
                "help",
                null,
                new[] { new GoapCondition(needsHelp, false) });

            var goal = Create<GoapGoalDefinition>();
            goal.Configure(
                "Resolve Need",
                10,
                new[] { new GoapCondition(needsHelp, true) },
                new[] { new GoapCondition(needsHelp, false) });

            var domain = Create<GoapDomain>();
            domain.AddFact(needsHelp);
            domain.AddAction(help);
            domain.AddGoal(goal);

            var agentObject = new GameObject("Test GOAP Agent");
            _created.Add(agentObject);
            var behaviour = agentObject.AddComponent<GoapImmediateTestActionBehaviour>();
            behaviour.SetExecutorId("help");
            var agent = agentObject.AddComponent<GoapAgent>();
            agent.Configure(domain, 0.05f);

            yield return new WaitForSeconds(0.25f);

            Assert.That(agent.WorldState.Get(needsHelp), Is.False);
            Assert.That(agent.CurrentAction, Is.Null);
            Assert.That(agent.CurrentGoal, Is.Null);
            Assert.That(agent.LastCompletedGoal, Is.SameAs(goal));

            Assert.That(
                agent.DecisionSnapshots.Any(snapshot => snapshot.Trigger == GoapTraceEventType.ActionSucceeded),
                Is.True);
            var initialSnapshot = agent.DecisionSnapshots.First(
                snapshot => snapshot.Trigger == GoapTraceEventType.Initialized);
            Assert.That(agent.RestoreDebugSnapshot(initialSnapshot), Is.True);
            Assert.That(agent.WorldState.Get(needsHelp), Is.True);
        }

        [UnityTest]
        public IEnumerator BuiltInActionGathersReservedResourceWithoutCustomExecutor()
        {
            var woodAvailable = Create<GoapFact>();
            woodAvailable.Configure("Wood Available", false);
            var woodCount = Create<GoapFact>();
            woodCount.ConfigureInteger("Wood Count", 0);

            var gather = Create<GoapActionDefinition>();
            gather.Configure(
                "Gather Wood",
                1f,
                string.Empty,
                new[]
                {
                    new GoapCondition(woodAvailable, true),
                    new GoapCondition(woodCount, 1, GoapComparison.Less)
                },
                new[]
                {
                    new GoapCondition(woodAvailable, false),
                    new GoapCondition(woodCount, 1, GoapComparison.Equal, GoapEffectOperation.Add)
                });
            gather.ConfigureBuiltInExecution(GoapBuiltInActionSettings.Interact(
                "Wood",
                0.05f,
                true,
                GoapInventoryOperation.Add,
                "Wood",
                1));

            var goal = Create<GoapGoalDefinition>();
            goal.Configure(
                "Collect Wood",
                10,
                null,
                new[] { new GoapCondition(woodCount, 1, GoapComparison.GreaterOrEqual) });

            var domain = Create<GoapDomain>();
            domain.AddFact(woodAvailable);
            domain.AddFact(woodCount);
            domain.AddAction(gather);
            domain.AddGoal(goal);

            var treeObject = new GameObject("Tree Smart Object");
            _created.Add(treeObject);
            treeObject.transform.position = Vector3.forward * 0.2f;
            var tree = treeObject.AddComponent<GoapSmartObject>();
            tree.Configure("Wood", true);

            var agentObject = new GameObject("Lumberjack Agent");
            _created.Add(agentObject);
            var inventory = agentObject.AddComponent<GoapInventory>();
            agentObject.AddComponent<GoapBuiltInActionBehaviour>();
            agentObject.AddComponent<GoapSmartObjectSensor>().Configure(new[]
            {
                new GoapSmartObjectFactBinding("Wood", woodAvailable)
            });
            agentObject.AddComponent<GoapInventorySensor>().Configure(new[]
            {
                new GoapInventoryFactBinding("Wood", woodCount)
            });
            var agent = agentObject.AddComponent<GoapAgent>();
            agent.Configure(domain, 0.05f);

            var timeout = Time.time + 2f;
            while (Time.time < timeout && inventory.GetAmount("Wood") == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.1f);
            Assert.That(inventory.GetAmount("Wood"), Is.EqualTo(1));
            Assert.That(tree.Available, Is.False);
            Assert.That(agent.LastCompletedGoal, Is.SameAs(goal));
        }

        [UnityTest]
        public IEnumerator BuiltInExecutorReportsQueuePositionWithoutJoiningQueue()
        {
            var targetObject = new GameObject("Diagnostic Bed");
            var ownerObject = new GameObject("Bed Owner");
            var candidateObject = new GameObject("Bed Candidate");
            _created.Add(targetObject);
            _created.Add(ownerObject);
            _created.Add(candidateObject);

            var target = targetObject.AddComponent<GoapSmartObject>();
            target.Configure("Diagnostic Bed", false, 1);
            var owner = ownerObject.AddComponent<GoapAgent>();
            var candidate = candidateObject.AddComponent<GoapAgent>();
            var behaviour = candidateObject.AddComponent<GoapBuiltInActionBehaviour>();
            var action = Create<GoapActionDefinition>();
            action.Configure("Sleep", 1f, string.Empty, null, null);
            action.ConfigureExecutionSteps(new[]
            {
                GoapActionStep.Find("Diagnostic Bed"),
                GoapActionStep.Reserve(5f),
                GoapActionStep.Wait(0.1f),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget)
            });

            yield return null;
            Assert.That(target.TryReserve(owner), Is.True);
            var diagnostic = behaviour.EvaluateStart(new GoapActionContext(candidate, action));

            Assert.That(
                diagnostic.Status,
                Is.EqualTo(GoapExecutorDiagnosticStatus.Warning),
                diagnostic.Message);
            Assert.That(diagnostic.Code, Is.EqualTo(GoapExecutorIssueCode.SmartObjectReserved));
            StringAssert.Contains("queue position 1", diagnostic.Message);
            Assert.That(target.QueueCount, Is.EqualTo(0), "Diagnostics must not mutate the reservation queue.");
        }

        [UnityTest]
        public IEnumerator PlanningBudgetEventuallyServesEveryQueuedAgent()
        {
            const int agentCount = 40;
            var needsHelp = Create<GoapFact>();
            needsHelp.Configure("Needs Help", true);
            var help = Create<GoapActionDefinition>();
            help.Configure(
                "Help",
                1f,
                "help",
                null,
                new[] { new GoapCondition(needsHelp, false) });
            var goal = Create<GoapGoalDefinition>();
            goal.Configure(
                "Resolve Need",
                10,
                new[] { new GoapCondition(needsHelp, true) },
                new[] { new GoapCondition(needsHelp, false) });
            var domain = Create<GoapDomain>();
            domain.AddFact(needsHelp);
            domain.AddAction(help);
            domain.AddGoal(goal);

            GoapPlanningScheduler.Configure(true, 4, 1000d);
            var agents = new List<GoapAgent>(agentCount);
            for (var index = 0; index < agentCount; index++)
            {
                var agentObject = new GameObject($"Budget Agent {index + 1}");
                _created.Add(agentObject);
                var behaviour = agentObject.AddComponent<GoapImmediateTestActionBehaviour>();
                behaviour.SetExecutorId("help");
                var agent = agentObject.AddComponent<GoapAgent>();
                agent.Configure(domain, 0.05f);
                agents.Add(agent);
            }

            var timeout = Time.time + 3f;
            while (Time.time < timeout && agents.Any(agent => agent.LastCompletedGoal != goal))
            {
                yield return null;
            }

            var metrics = GoapPlanningScheduler.Metrics;
            Assert.That(agents.All(agent => agent.LastCompletedGoal == goal), Is.True);
            Assert.That(metrics.TotalDeferred, Is.GreaterThan(0));
            Assert.That(metrics.PeakPlansPerFrame, Is.LessThanOrEqualTo(4));
            Assert.That(metrics.QueuedRequests, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BenchmarkModeSwitchesAgentAndEnvironmentRenderers()
        {
            var domain = Create<GoapDomain>();
            var profile = Create<GoapAgentProfile>();
            profile.Configure(domain);
            var benchmarkObject = new GameObject("Benchmark Mode Test");
            _created.Add(benchmarkObject);
            var environment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            environment.transform.SetParent(benchmarkObject.transform);
            var environmentRenderer = environment.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var idle = new Material(shader);
            var queued = new Material(shader);
            var active = new Material(shader);
            var completed = new Material(shader);
            _created.Add(idle);
            _created.Add(queued);
            _created.Add(active);
            _created.Add(completed);

            var runner = benchmarkObject.AddComponent<GoapBenchmarkRunner>();
            runner.Configure(profile, 5);
            runner.ConfigureVisualization(idle, queued, active, completed, environmentRenderer);
            runner.SetMode(GoapBenchmarkMode.LogicOnly);

            yield return null;

            Assert.That(runner.Agents, Has.Count.EqualTo(5));
            Assert.That(environmentRenderer.enabled, Is.False);
            Assert.That(runner.Agents.All(agent => !agent.GetComponent<Renderer>().enabled), Is.True);

            runner.SetMode(GoapBenchmarkMode.Visual);

            Assert.That(environmentRenderer.enabled, Is.True);
            Assert.That(runner.Agents.All(agent => agent.GetComponent<Renderer>().enabled), Is.True);
        }

        [UnityTest]
        public IEnumerator DemoSceneBootstrapsFiveNpcAndCompletesProfileDrivenGoals()
        {
            yield return SceneManager.LoadSceneAsync("GOAP Demo", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.25f);

            var agents = Object.FindObjectsByType<GoapAgent>(FindObjectsSortMode.None);
            Assert.That(agents.Length, Is.GreaterThanOrEqualTo(5));

            var goalsByAgent = new Dictionary<string, string>();
            foreach (var agent in agents)
            {
                goalsByAgent[agent.name] = agent.CurrentGoal != null ? agent.CurrentGoal.DisplayName : string.Empty;
            }

            var requiredAgents = new[]
            {
                "Worker NPC",
                "Resident NPC",
                "Guard NPC",
                "Survivor NPC",
                "Lumberjack NPC"
            };
            Assert.That(goalsByAgent.Keys, Is.SupersetOf(requiredAgents));

            Assert.That(goalsByAgent["Worker NPC"], Is.EqualTo("Satisfy Hunger"));
            Assert.That(goalsByAgent["Resident NPC"], Is.EqualTo("Recover Energy"));
            Assert.That(goalsByAgent["Guard NPC"], Is.EqualTo("Defeat Enemy"));
            Assert.That(goalsByAgent["Survivor NPC"], Is.EqualTo("Satisfy Hunger"));
            Assert.That(goalsByAgent["Lumberjack NPC"], Is.EqualTo("Collect Wood"));

            var survivor = System.Array.Find(agents, agent => agent.name == "Survivor NPC");
            var hungry = survivor.Domain.FindFact("Is Hungry");
            var tired = survivor.Domain.FindFact("Is Tired");
            var timeout = Time.time + 15f;
            while (Time.time < timeout && (survivor.WorldState.Get(hungry) || survivor.WorldState.Get(tired)))
            {
                yield return null;
            }

            Assert.That(survivor.WorldState.Get(hungry), Is.False, "Survivor did not finish the hunger goal.");
            Assert.That(survivor.WorldState.Get(tired), Is.False, "Survivor did not finish the rest goal.");
            Assert.That(survivor.LastCompletedGoal.DisplayName, Is.EqualTo("Recover Energy"));

            var lumberjack = System.Array.Find(agents, agent => agent.name == "Lumberjack NPC");
            var woodCount = lumberjack.Domain.FindFact("Wood Count");
            timeout = Time.time + 5f;
            while (Time.time < timeout && lumberjack.WorldState.GetInteger(woodCount) < 1)
            {
                yield return null;
            }

            Assert.That(lumberjack.WorldState.GetInteger(woodCount), Is.GreaterThanOrEqualTo(1));
            Assert.That(lumberjack.LastCompletedGoal.DisplayName, Is.EqualTo("Collect Wood"));
        }

        private T Create<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }
    }
}
