using NUnit.Framework;

namespace Practice.GOAP.Tests
{
    public sealed class GoapPlanningSchedulerTests
    {
        [TearDown]
        public void TearDown()
        {
            GoapPlanningScheduler.RestoreDefaults();
        }

        [Test]
        public void DefersRequestsAfterFrameLimit()
        {
            GoapPlanningScheduler.Configure(true, 2, 1000d);

            Assert.That(GoapPlanningScheduler.TryAcquire(), Is.True);
            Assert.That(GoapPlanningScheduler.TryAcquire(), Is.True);
            Assert.That(GoapPlanningScheduler.TryAcquire(), Is.False);

            var metrics = GoapPlanningScheduler.Metrics;
            Assert.That(metrics.RequestsThisFrame, Is.EqualTo(3));
            Assert.That(metrics.GrantedThisFrame, Is.EqualTo(2));
            Assert.That(metrics.DeferredThisFrame, Is.EqualTo(1));
            Assert.That(metrics.TotalDeferred, Is.EqualTo(1));
        }

        [Test]
        public void DefersRequestsAfterTimeBudget()
        {
            GoapPlanningScheduler.Configure(true, 10, 1d);

            Assert.That(GoapPlanningScheduler.TryAcquire(), Is.True);
            GoapPlanningScheduler.Report(1.1d, true, 12);
            Assert.That(GoapPlanningScheduler.TryAcquire(), Is.False);

            var metrics = GoapPlanningScheduler.Metrics;
            Assert.That(metrics.CompletedPlans, Is.EqualTo(1));
            Assert.That(metrics.SuccessfulPlans, Is.EqualTo(1));
            Assert.That(metrics.ExpandedStates, Is.EqualTo(12));
            Assert.That(metrics.TotalDeferred, Is.EqualTo(1));
        }

        [Test]
        public void CancelRemovesRequesterFromFairnessQueue()
        {
            GoapPlanningScheduler.Configure(true, 1, 1000d);

            Assert.That(GoapPlanningScheduler.TryAcquire(101), Is.True);
            Assert.That(GoapPlanningScheduler.TryAcquire(202), Is.False);
            Assert.That(GoapPlanningScheduler.Metrics.QueuedRequests, Is.EqualTo(1));

            GoapPlanningScheduler.Cancel(202);

            Assert.That(GoapPlanningScheduler.Metrics.QueuedRequests, Is.Zero);
        }
    }
}
