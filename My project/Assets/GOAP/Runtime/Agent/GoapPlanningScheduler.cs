using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Practice.GOAP
{
    public readonly struct GoapPlanningSchedulerMetrics
    {
        public int Frame { get; }
        public int RequestsThisFrame { get; }
        public int GrantedThisFrame { get; }
        public int DeferredThisFrame { get; }
        public int QueuedRequests { get; }
        public double PlanningMillisecondsThisFrame { get; }
        public long TotalRequests { get; }
        public long TotalGranted { get; }
        public long TotalDeferred { get; }
        public long CompletedPlans { get; }
        public long SuccessfulPlans { get; }
        public long FailedPlans { get; }
        public long ExpandedStates { get; }
        public double TotalPlanningMilliseconds { get; }
        public double PeakPlanningMillisecondsPerFrame { get; }
        public int PeakPlansPerFrame { get; }
        public double AveragePlanningMilliseconds => CompletedPlans == 0
            ? 0d
            : TotalPlanningMilliseconds / CompletedPlans;

        internal GoapPlanningSchedulerMetrics(
            int frame,
            int requestsThisFrame,
            int grantedThisFrame,
            int deferredThisFrame,
            int queuedRequests,
            double planningMillisecondsThisFrame,
            long totalRequests,
            long totalGranted,
            long totalDeferred,
            long completedPlans,
            long successfulPlans,
            long failedPlans,
            long expandedStates,
            double totalPlanningMilliseconds,
            double peakPlanningMillisecondsPerFrame,
            int peakPlansPerFrame)
        {
            Frame = frame;
            RequestsThisFrame = requestsThisFrame;
            GrantedThisFrame = grantedThisFrame;
            DeferredThisFrame = deferredThisFrame;
            QueuedRequests = queuedRequests;
            PlanningMillisecondsThisFrame = planningMillisecondsThisFrame;
            TotalRequests = totalRequests;
            TotalGranted = totalGranted;
            TotalDeferred = totalDeferred;
            CompletedPlans = completedPlans;
            SuccessfulPlans = successfulPlans;
            FailedPlans = failedPlans;
            ExpandedStates = expandedStates;
            TotalPlanningMilliseconds = totalPlanningMilliseconds;
            PeakPlanningMillisecondsPerFrame = peakPlanningMillisecondsPerFrame;
            PeakPlansPerFrame = peakPlansPerFrame;
        }
    }

    public static class GoapPlanningScheduler
    {
        public const int DefaultMaxPlansPerFrame = 16;
        public const double DefaultMaxPlanningMillisecondsPerFrame = 4d;

        private static readonly ProfilerMarker BudgetMarker = new("GOAP.Scheduler.BudgetCheck");
        private static readonly Queue<int> WaitingRequesters = new();
        private static readonly HashSet<int> WaitingRequesterIds = new();
        private static int _frame = -1;
        private static int _requestsThisFrame;
        private static int _grantedThisFrame;
        private static int _deferredThisFrame;
        private static double _planningMillisecondsThisFrame;
        private static long _totalRequests;
        private static long _totalGranted;
        private static long _totalDeferred;
        private static long _completedPlans;
        private static long _successfulPlans;
        private static long _failedPlans;
        private static long _expandedStates;
        private static double _totalPlanningMilliseconds;
        private static double _peakPlanningMillisecondsPerFrame;
        private static int _peakPlansPerFrame;

        public static bool Enabled { get; private set; } = true;
        public static int MaxPlansPerFrame { get; private set; } = DefaultMaxPlansPerFrame;
        public static double MaxPlanningMillisecondsPerFrame { get; private set; } =
            DefaultMaxPlanningMillisecondsPerFrame;

        public static GoapPlanningSchedulerMetrics Metrics
        {
            get
            {
                RefreshFrame();
                return new GoapPlanningSchedulerMetrics(
                    _frame,
                    _requestsThisFrame,
                    _grantedThisFrame,
                    _deferredThisFrame,
                    WaitingRequesters.Count,
                    _planningMillisecondsThisFrame,
                    _totalRequests,
                    _totalGranted,
                    _totalDeferred,
                    _completedPlans,
                    _successfulPlans,
                    _failedPlans,
                    _expandedStates,
                    _totalPlanningMilliseconds,
                    _peakPlanningMillisecondsPerFrame,
                    _peakPlansPerFrame);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeStart()
        {
            RestoreDefaults();
        }

        public static void Configure(bool enabled, int maxPlansPerFrame, double maxPlanningMillisecondsPerFrame)
        {
            Enabled = enabled;
            MaxPlansPerFrame = Math.Max(1, maxPlansPerFrame);
            MaxPlanningMillisecondsPerFrame = Math.Max(0.1d, maxPlanningMillisecondsPerFrame);
            ResetMetrics();
        }

        public static bool TryAcquire()
        {
            return TryAcquire(0);
        }

        public static bool TryAcquire(int requesterId)
        {
            using var marker = BudgetMarker.Auto();
            RefreshFrame();
            _requestsThisFrame++;
            _totalRequests++;

            if (Enabled && requesterId != 0 && WaitingRequesterIds.Add(requesterId))
            {
                WaitingRequesters.Enqueue(requesterId);
            }

            var requesterHasTurn = requesterId == 0 ||
                                   WaitingRequesters.Count == 0 ||
                                   WaitingRequesters.Peek() == requesterId;
            var budgetAvailable = !Enabled ||
                                  (requesterHasTurn &&
                                   _grantedThisFrame < MaxPlansPerFrame &&
                                   _planningMillisecondsThisFrame < MaxPlanningMillisecondsPerFrame);
            if (!budgetAvailable)
            {
                _deferredThisFrame++;
                _totalDeferred++;
                return false;
            }

            _grantedThisFrame++;
            _totalGranted++;
            _peakPlansPerFrame = Math.Max(_peakPlansPerFrame, _grantedThisFrame);
            if (requesterId != 0 && WaitingRequesterIds.Remove(requesterId))
            {
                WaitingRequesters.Dequeue();
            }

            return true;
        }

        public static void Cancel(int requesterId)
        {
            if (requesterId == 0 || !WaitingRequesterIds.Remove(requesterId))
            {
                return;
            }

            var count = WaitingRequesters.Count;
            for (var index = 0; index < count; index++)
            {
                var queuedId = WaitingRequesters.Dequeue();
                if (queuedId != requesterId)
                {
                    WaitingRequesters.Enqueue(queuedId);
                }
            }
        }

        public static void Report(double planningMilliseconds, bool success, int expandedStates)
        {
            RefreshFrame();
            var elapsed = Math.Max(0d, planningMilliseconds);
            _planningMillisecondsThisFrame += elapsed;
            _totalPlanningMilliseconds += elapsed;
            _completedPlans++;
            _expandedStates += Math.Max(0, expandedStates);
            if (success)
            {
                _successfulPlans++;
            }
            else
            {
                _failedPlans++;
            }

            _peakPlanningMillisecondsPerFrame = Math.Max(
                _peakPlanningMillisecondsPerFrame,
                _planningMillisecondsThisFrame);
        }

        public static void ResetMetrics()
        {
            _frame = Time.frameCount;
            _requestsThisFrame = 0;
            _grantedThisFrame = 0;
            _deferredThisFrame = 0;
            _planningMillisecondsThisFrame = 0d;
            _totalRequests = 0;
            _totalGranted = 0;
            _totalDeferred = 0;
            _completedPlans = 0;
            _successfulPlans = 0;
            _failedPlans = 0;
            _expandedStates = 0;
            _totalPlanningMilliseconds = 0d;
            _peakPlanningMillisecondsPerFrame = 0d;
            _peakPlansPerFrame = 0;
            WaitingRequesters.Clear();
            WaitingRequesterIds.Clear();
        }

        public static void RestoreDefaults()
        {
            Enabled = true;
            MaxPlansPerFrame = DefaultMaxPlansPerFrame;
            MaxPlanningMillisecondsPerFrame = DefaultMaxPlanningMillisecondsPerFrame;
            ResetMetrics();
        }

        private static void RefreshFrame()
        {
            if (_frame == Time.frameCount)
            {
                return;
            }

            _frame = Time.frameCount;
            _requestsThisFrame = 0;
            _grantedThisFrame = 0;
            _deferredThisFrame = 0;
            _planningMillisecondsThisFrame = 0d;
        }
    }
}
