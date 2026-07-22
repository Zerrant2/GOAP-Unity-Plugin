using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapActionInterruptionPolicy
    {
        Immediate,
        FinishCurrentAction,
        FinishCurrentPlan
    }

    public enum GoapActionTargetMode
    {
        Automatic,
        None,
        SmartObjectCategory,
        NamedTarget
    }

    public readonly struct GoapActionTargetDescriptor
    {
        public GoapActionTargetMode Mode { get; }
        public string Identifier { get; }
        public bool IncludeBusySmartObjects { get; }

        public GoapActionTargetDescriptor(
            GoapActionTargetMode mode,
            string identifier,
            bool includeBusySmartObjects)
        {
            Mode = mode;
            Identifier = identifier ?? string.Empty;
            IncludeBusySmartObjects = includeBusySmartObjects;
        }
    }

    [CreateAssetMenu(menuName = "GOAP/Action", fileName = "New Action")]
    public sealed class GoapActionDefinition : GoapDefinition
    {
        [SerializeField, Min(0.01f)] private float _cost = 1f;
        [SerializeField] private GoapActionInterruptionPolicy _interruptionPolicy;
        [SerializeField] private GoapActionTargetMode _targetMode = GoapActionTargetMode.Automatic;
        [SerializeField] private string _planningTargetId = string.Empty;
        [SerializeField] private bool _includeBusySmartObjects;
        [SerializeField, Min(0f)] private float _distanceCostPerUnit;
        [SerializeField] private string _executorId;
        [SerializeField] private GoapBuiltInActionSettings _builtInExecution;
        [SerializeField] private List<GoapActionStep> _executionSteps = new();
        [SerializeField] private List<GoapCondition> _preconditions = new();
        [SerializeField] private List<GoapCondition> _effects = new();

        public float Cost => Mathf.Max(0.01f, _cost);
        public GoapActionInterruptionPolicy InterruptionPolicy => _interruptionPolicy;
        public GoapActionTargetMode TargetMode => _targetMode;
        public float DistanceCostPerUnit => Mathf.Max(0f, _distanceCostPerUnit);
        public string ExecutorId => _executorId;
        public GoapBuiltInActionSettings BuiltInExecution => _builtInExecution;
        public IReadOnlyList<GoapActionStep> ExecutionSteps => _executionSteps;
        public bool UsesBuiltInExecutor => _builtInExecution.IsConfigured;
        public IReadOnlyList<GoapCondition> Preconditions => _preconditions;
        public IReadOnlyList<GoapCondition> Effects => _effects;

        public void Configure(
            string displayName,
            float cost,
            string executorId,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects,
            string description = "")
        {
            SetIdentity(displayName, description);
            _cost = Mathf.Max(0.01f, cost);
            _executorId = executorId;
            _builtInExecution = default;
            _executionSteps = new List<GoapActionStep>();
            _preconditions = preconditions == null ? new List<GoapCondition>() : new List<GoapCondition>(preconditions);
            _effects = effects == null ? new List<GoapCondition>() : new List<GoapCondition>(effects);
        }

        public void ConfigureBuiltInExecution(GoapBuiltInActionSettings settings)
        {
            _builtInExecution = settings;
        }

        public void ConfigureInterruption(GoapActionInterruptionPolicy policy)
        {
            _interruptionPolicy = policy;
        }

        public void ConfigureTargeting(
            GoapActionTargetMode mode,
            string targetId = "",
            bool includeBusySmartObjects = false,
            float distanceCostPerUnit = 0f)
        {
            _targetMode = mode;
            _planningTargetId = targetId ?? string.Empty;
            _includeBusySmartObjects = includeBusySmartObjects;
            _distanceCostPerUnit = Mathf.Max(0f, distanceCostPerUnit);
        }

        public bool TryGetPlanningTarget(out GoapActionTargetDescriptor descriptor)
        {
            if (_targetMode == GoapActionTargetMode.SmartObjectCategory ||
                _targetMode == GoapActionTargetMode.NamedTarget)
            {
                descriptor = new GoapActionTargetDescriptor(
                    _targetMode,
                    _planningTargetId,
                    _includeBusySmartObjects);
                return !string.IsNullOrWhiteSpace(_planningTargetId);
            }

            if (_targetMode == GoapActionTargetMode.None)
            {
                descriptor = default;
                return false;
            }

            if (_builtInExecution.Mode == GoapExecutionMode.SmartObjectInteraction &&
                !string.IsNullOrWhiteSpace(_builtInExecution.TargetCategory))
            {
                descriptor = new GoapActionTargetDescriptor(
                    GoapActionTargetMode.SmartObjectCategory,
                    _builtInExecution.TargetCategory,
                    false);
                return true;
            }

            if (_builtInExecution.Mode == GoapExecutionMode.Sequence)
            {
                var findStep = _executionSteps.FirstOrDefault(step =>
                    step != null && step.Kind == GoapActionStepKind.FindSmartObject);
                if (findStep != null && !string.IsNullOrWhiteSpace(findStep.TargetCategory))
                {
                    descriptor = new GoapActionTargetDescriptor(
                        GoapActionTargetMode.SmartObjectCategory,
                        findStep.TargetCategory,
                        true);
                    return true;
                }

                var moveStep = _executionSteps.FirstOrDefault(step =>
                    step != null && step.Kind == GoapActionStepKind.MoveToTarget &&
                    !string.IsNullOrWhiteSpace(step.TargetId));
                if (moveStep != null)
                {
                    descriptor = new GoapActionTargetDescriptor(
                        GoapActionTargetMode.NamedTarget,
                        moveStep.TargetId,
                        false);
                    return true;
                }
            }

            descriptor = default;
            return false;
        }

        public void ConfigureExecutionSteps(IEnumerable<GoapActionStep> steps)
        {
            _executionSteps = steps == null ? new List<GoapActionStep>() : new List<GoapActionStep>(steps);
            _builtInExecution = GoapBuiltInActionSettings.Sequence();
        }

        public bool AddPrecondition(GoapCondition condition)
        {
            if (!condition.IsValid || _preconditions.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _preconditions.Add(condition);
            return true;
        }

        public bool AddEffect(GoapCondition condition)
        {
            if (!condition.IsValid || _effects.Any(item => item.Fact == condition.Fact))
            {
                return false;
            }

            _effects.Add(condition);
            return true;
        }

        public bool RemovePrecondition(GoapFact fact)
        {
            return _preconditions.RemoveAll(item => item.Fact == fact) > 0;
        }

        public bool RemoveEffect(GoapFact fact)
        {
            return _effects.RemoveAll(item => item.Fact == fact) > 0;
        }
    }
}
