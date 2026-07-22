using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Practice.GOAP
{
    [DisallowMultipleComponent]
    public sealed class GoapBuiltInActionBehaviour : GoapActionBehaviour
    {
        private GoapSmartObject _target;
        private Transform _namedTarget;
        private GoapAgent _targetOwner;
        private GoapBuiltInActionSettings _settings;
        private bool _waitingForReservation;

        public override bool Supports(GoapActionDefinition action)
        {
            return action != null && action.UsesBuiltInExecutor;
        }

        public override GoapExecutorDiagnostic EvaluateStart(GoapActionContext context)
        {
            var baseDiagnostic = base.EvaluateStart(context);
            if (!baseDiagnostic.CanStart)
            {
                return baseDiagnostic;
            }

            if (context?.Definition == null || context.Agent == null)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "Action context or Agent is missing");
            }

            var settings = context.Definition.BuiltInExecution;
            return settings.Mode switch
            {
                GoapExecutionMode.Wait => EvaluateWait(settings),
                GoapExecutionMode.SmartObjectInteraction => EvaluateSmartObjectInteraction(context, settings),
                GoapExecutionMode.Sequence => EvaluateSequence(context),
                _ => GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "Built-in execution mode is not configured")
            };
        }

        public override bool CanStart(GoapActionContext context)
        {
            if (!base.CanStart(context))
            {
                return false;
            }

            ReleaseTarget();
            _settings = context.Definition.BuiltInExecution;
            _targetOwner = context.Agent;

            if (_settings.Mode == GoapExecutionMode.Sequence)
            {
                return true;
            }

            if (_settings.Mode == GoapExecutionMode.SmartObjectInteraction)
            {
                if (string.IsNullOrWhiteSpace(_settings.TargetCategory))
                {
                    return false;
                }

                _target = ResolveContextSmartObject(context, _settings.TargetCategory, false) ??
                          GoapSmartObject.FindClosest(
                              _settings.TargetCategory,
                              transform.position,
                              context.Agent);
                if (_target == null || (_settings.ReserveTarget && !_target.TryReserve(context.Agent)))
                {
                    _target = null;
                    return false;
                }
            }

            return CanApplyInventory(
                _settings.InventoryOperation,
                _settings.InventoryItemId,
                _settings.InventoryAmount);
        }

        public override bool CanContinue(GoapActionContext context)
        {
            if (!base.CanContinue(context) || (_target != null && !_target.Available))
            {
                return false;
            }

            if (_target == null)
            {
                return true;
            }

            if (_waitingForReservation)
            {
                return _target.IsQueued(context.Agent) || _target.IsReservedBy(context.Agent) ||
                       _target.Available;
            }

            var reservationRequired = _settings.Mode == GoapExecutionMode.Sequence || _settings.ReserveTarget;
            return !reservationRequired ||
                   !_target.IsReservedBy(context.Agent) ||
                   _target.RefreshReservation(context.Agent);
        }

        protected override IEnumerator Perform(GoapActionContext context)
        {
            if (_settings.Mode == GoapExecutionMode.Sequence)
            {
                yield return PerformSequence(context);
                yield break;
            }

            TriggerAnimation(_settings.AnimatorTrigger);

            if (_target != null && _settings.MoveToTarget)
            {
                yield return MoveTo(context, _target.transform, _settings.InteractionRange, _settings.MoveSpeed, _settings.UseNavMesh);
                if (Status != GoapActionStatus.Running)
                {
                    ReleaseTarget();
                    yield break;
                }
            }

            yield return WaitWithValidation(context, _settings.Duration);
            if (Status != GoapActionStatus.Running)
            {
                ReleaseTarget();
                yield break;
            }

            if (_settings.ConsumeTarget && (_target == null || !_target.TryUse(context.Agent)))
            {
                FailAndRelease("SmartObject target is unavailable, consumed, or reserved by another Agent");
                yield break;
            }

            if (!ApplyInventoryOperation(
                    _settings.InventoryOperation,
                    _settings.InventoryItemId,
                    _settings.InventoryAmount))
            {
                FailAndRelease(
                    $"Inventory operation '{_settings.InventoryOperation}' failed for " +
                    $"{_settings.InventoryAmount} x '{_settings.InventoryItemId}'");
                yield break;
            }

            ReleaseTarget();
            Succeed();
        }

        protected override void OnCancelled(GoapActionContext context)
        {
            StopNavigation();
            ReleaseTarget();
        }

        private GoapExecutorDiagnostic EvaluateWait(GoapBuiltInActionSettings settings)
        {
            var warnings = new List<GoapExecutorDiagnostic>();
            AddWarning(warnings, EvaluateAnimator(settings.AnimatorTrigger, false));
            return CombineWarnings(warnings);
        }

        private GoapExecutorDiagnostic EvaluateSmartObjectInteraction(
            GoapActionContext context,
            GoapBuiltInActionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.TargetCategory))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "SmartObject target category is empty");
            }

            var target = ResolveContextSmartObject(context, settings.TargetCategory, false) ??
                         GoapSmartObject.FindClosest(
                             settings.TargetCategory,
                             transform.position,
                             context.Agent);
            if (target == null)
            {
                var busyTarget = GoapSmartObject.FindClosest(
                    settings.TargetCategory,
                    transform.position,
                    context.Agent,
                    float.PositiveInfinity,
                    true);
                if (busyTarget != null)
                {
                    return GoapExecutorDiagnostic.Blocked(
                        GoapExecutorIssueCode.SmartObjectReserved,
                        $"All '{settings.TargetCategory}' SmartObjects are reserved " +
                        $"({busyTarget.ReservedCount}/{busyTarget.Capacity}); this Action does not queue");
                }

                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.SmartObjectNotFound,
                    $"No available SmartObject with category '{settings.TargetCategory}' was found");
            }

            var inventoryDiagnostic = EvaluateInventory(
                settings.InventoryOperation,
                settings.InventoryItemId,
                settings.InventoryAmount);
            if (!inventoryDiagnostic.CanStart)
            {
                return inventoryDiagnostic;
            }

            var warnings = new List<GoapExecutorDiagnostic>();
            if (settings.MoveToTarget)
            {
                var navigationDiagnostic = EvaluateNavigation(target.transform, settings.UseNavMesh);
                if (!navigationDiagnostic.CanStart)
                {
                    return navigationDiagnostic;
                }

                AddWarning(warnings, navigationDiagnostic);
            }

            AddWarning(warnings, EvaluateAnimator(settings.AnimatorTrigger, false));
            return CombineWarnings(warnings);
        }

        private GoapExecutorDiagnostic EvaluateSequence(GoapActionContext context)
        {
            var steps = context.Definition.ExecutionSteps;
            if (steps == null || steps.Count == 0)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "Execution sequence has no steps");
            }

            var warnings = new List<GoapExecutorDiagnostic>();
            var requiredInventory = new Dictionary<string, int>();
            var inventory = GetComponent<GoapInventory>();
            GoapSmartObject target = null;
            var reservationPlanned = false;

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var stepNumber = index + 1;
                if (step == null)
                {
                    warnings.Add(GoapExecutorDiagnostic.Warning(
                        GoapExecutorIssueCode.InvalidConfiguration,
                        $"Step {stepNumber} is missing and will be skipped"));
                    continue;
                }

                switch (step.Kind)
                {
                    case GoapActionStepKind.FindSmartObject:
                        if (string.IsNullOrWhiteSpace(step.TargetCategory))
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.InvalidConfiguration,
                                "SmartObject category is empty");
                        }

                        target = ResolveContextSmartObject(context, step.TargetCategory, true) ??
                                 GoapSmartObject.FindClosest(
                                     step.TargetCategory,
                                     transform.position,
                                     context.Agent,
                                     float.PositiveInfinity,
                                     true);
                        reservationPlanned = false;
                        if (target == null)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.SmartObjectNotFound,
                                $"no available SmartObject with category '{step.TargetCategory}' was found");
                        }
                        break;

                    case GoapActionStepKind.ReserveTarget:
                        if (target == null)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.TargetMissing,
                                "there is no target; add Find Smart Object before Reserve Target");
                        }

                        reservationPlanned = true;
                        if (!target.IsAvailableTo(context.Agent) && !target.IsReservedBy(context.Agent))
                        {
                            var queuePosition = target.GetQueuePosition(context.Agent);
                            if (queuePosition == 0)
                            {
                                queuePosition = target.QueueCount + 1;
                            }

                            warnings.Add(GoapExecutorDiagnostic.Warning(
                                GoapExecutorIssueCode.SmartObjectReserved,
                                $"Step {stepNumber}: '{target.name}' is reserved " +
                                $"({target.ReservedCount}/{target.Capacity}); Agent will wait at queue position {queuePosition}"));
                        }
                        break;

                    case GoapActionStepKind.MoveToTarget:
                        var destination = string.IsNullOrWhiteSpace(step.TargetId)
                            ? target != null ? target.transform : null
                            : context.NamedTarget != null
                                ? context.NamedTarget
                                : ResolveNamedTarget(context.Agent, step.TargetId);
                        if (destination == null)
                        {
                            var targetDescription = string.IsNullOrWhiteSpace(step.TargetId)
                                ? "there is no current SmartObject target"
                                : $"named target '{step.TargetId}' is not assigned in GoapAgentAuthoring";
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.TargetMissing,
                                targetDescription);
                        }

                        var navigationDiagnostic = EvaluateNavigation(destination, step.UseNavMesh);
                        if (!navigationDiagnostic.CanStart)
                        {
                            return BlockedStep(stepNumber, navigationDiagnostic.Code, navigationDiagnostic.Message);
                        }

                        AddWarning(warnings, PrefixStep(stepNumber, navigationDiagnostic));
                        break;

                    case GoapActionStepKind.Interact:
                    case GoapActionStepKind.ConsumeTarget:
                        if (target == null)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.TargetMissing,
                                $"{step.Kind} requires a SmartObject target");
                        }

                        if (!reservationPlanned && !target.IsAvailableTo(context.Agent))
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.SmartObjectReserved,
                                $"'{target.name}' is reserved by another Agent");
                        }
                        break;

                    case GoapActionStepKind.ReleaseTarget:
                        target = null;
                        reservationPlanned = false;
                        break;

                    case GoapActionStepKind.InventoryAdd:
                    case GoapActionStepKind.InventoryRemove:
                        if (string.IsNullOrWhiteSpace(step.ItemId))
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.InvalidConfiguration,
                                "Inventory Item ID is empty");
                        }

                        if (inventory == null)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.InventoryMissing,
                                "GoapInventory component is missing");
                        }

                        if (step.Kind == GoapActionStepKind.InventoryRemove)
                        {
                            requiredInventory.TryGetValue(step.ItemId, out var alreadyRequired);
                            var totalRequired = alreadyRequired + step.Amount;
                            var available = inventory.GetAmount(step.ItemId);
                            if (available < totalRequired)
                            {
                                return BlockedStep(
                                    stepNumber,
                                    GoapExecutorIssueCode.InventoryInsufficient,
                                    $"Inventory '{step.ItemId}' has {available}, but the sequence requires {totalRequired}");
                            }

                            requiredInventory[step.ItemId] = totalRequired;
                        }
                        break;

                    case GoapActionStepKind.SetFact:
                    case GoapActionStepKind.AddFact:
                    case GoapActionStepKind.SubtractFact:
                        if (!step.FactValue.IsValid)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.InvalidConfiguration,
                                $"{step.Kind} has no Fact");
                        }

                        if (step.Kind != GoapActionStepKind.SetFact &&
                            step.FactValue.Fact.ValueType != GoapFactType.Integer &&
                            step.FactValue.Fact.ValueType != GoapFactType.Float)
                        {
                            return BlockedStep(
                                stepNumber,
                                GoapExecutorIssueCode.InvalidConfiguration,
                                $"{step.Kind} requires an Integer or Float Fact");
                        }
                        break;

                    case GoapActionStepKind.TriggerAnimation:
                        var animatorDiagnostic = EvaluateAnimator(step.EventId, true);
                        if (!animatorDiagnostic.CanStart)
                        {
                            return BlockedStep(stepNumber, animatorDiagnostic.Code, animatorDiagnostic.Message);
                        }

                        AddWarning(warnings, PrefixStep(stepNumber, animatorDiagnostic));
                        break;

                    case GoapActionStepKind.InvokeEvent:
                        var eventDiagnostic = EvaluateEvent(step.EventId);
                        if (!eventDiagnostic.CanStart)
                        {
                            return BlockedStep(stepNumber, eventDiagnostic.Code, eventDiagnostic.Message);
                        }
                        break;
                }
            }

            return CombineWarnings(warnings);
        }

        private GoapExecutorDiagnostic EvaluateInventory(
            GoapInventoryOperation operation,
            string itemId,
            int amount)
        {
            if (operation == GoapInventoryOperation.None)
            {
                return GoapExecutorDiagnostic.Ready();
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "Inventory Item ID is empty");
            }

            if (!TryGetComponent<GoapInventory>(out var inventory))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InventoryMissing,
                    "GoapInventory component is missing");
            }

            if (operation == GoapInventoryOperation.Remove && inventory.GetAmount(itemId) < amount)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InventoryInsufficient,
                    $"Inventory '{itemId}' has {inventory.GetAmount(itemId)}, but {amount} is required");
            }

            return GoapExecutorDiagnostic.Ready();
        }

        private GoapExecutorDiagnostic EvaluateNavigation(Transform destination, bool useNavMesh)
        {
            if (!useNavMesh)
            {
                return GoapExecutorDiagnostic.Ready();
            }

            if (destination == null)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.TargetMissing,
                    "Movement destination is missing");
            }

            if (!TryGetComponent<NavMeshAgent>(out var navigation))
            {
                return GoapExecutorDiagnostic.Warning(
                    GoapExecutorIssueCode.NavMeshAgentMissing,
                    "NavMeshAgent is missing; direct movement fallback will be used");
            }

            if (!navigation.enabled)
            {
                return GoapExecutorDiagnostic.Warning(
                    GoapExecutorIssueCode.NavMeshAgentDisabled,
                    "NavMeshAgent is disabled; direct movement fallback will be used");
            }

            if (!navigation.isOnNavMesh)
            {
                return GoapExecutorDiagnostic.Warning(
                    GoapExecutorIssueCode.NavMeshNotReady,
                    "NavMeshAgent is not on a NavMesh; direct movement fallback will be used");
            }

            var path = new NavMeshPath();
            if (!navigation.CalculatePath(destination.position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.NavMeshPathInvalid,
                    $"No complete NavMesh path to '{destination.name}'");
            }

            return GoapExecutorDiagnostic.Ready();
        }

        private GoapExecutorDiagnostic EvaluateAnimator(string triggerName, bool required)
        {
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return required
                    ? GoapExecutorDiagnostic.Blocked(
                        GoapExecutorIssueCode.InvalidConfiguration,
                        "Animator trigger is empty")
                    : GoapExecutorDiagnostic.Ready();
            }

            if (!TryGetComponent<Animator>(out var animator))
            {
                return GoapExecutorDiagnostic.Warning(
                    GoapExecutorIssueCode.AnimatorMissing,
                    $"Animator is missing; trigger '{triggerName}' will be skipped");
            }

            var hasTrigger = animator.parameters.Any(parameter =>
                parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName);
            return hasTrigger
                ? GoapExecutorDiagnostic.Ready()
                : GoapExecutorDiagnostic.Warning(
                    GoapExecutorIssueCode.AnimatorTriggerMissing,
                    $"Animator has no Trigger parameter named '{triggerName}'");
        }

        private GoapExecutorDiagnostic EvaluateEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.InvalidConfiguration,
                    "Event ID is empty");
            }

            if (!TryGetComponent<GoapActionEventReceiver>(out var receiver))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.EventReceiverMissing,
                    "GoapActionEventReceiver component is missing");
            }

            return receiver.HasEvent(eventId)
                ? GoapExecutorDiagnostic.Ready()
                : GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.EventMissing,
                    $"GoapActionEventReceiver has no event with ID '{eventId}'");
        }

        private static GoapExecutorDiagnostic BlockedStep(
            int stepNumber,
            GoapExecutorIssueCode code,
            string message)
        {
            return GoapExecutorDiagnostic.Blocked(code, $"Step {stepNumber}: {message}");
        }

        private static GoapExecutorDiagnostic PrefixStep(
            int stepNumber,
            GoapExecutorDiagnostic diagnostic)
        {
            return diagnostic.Status == GoapExecutorDiagnosticStatus.Warning
                ? GoapExecutorDiagnostic.Warning(diagnostic.Code, $"Step {stepNumber}: {diagnostic.Message}")
                : diagnostic;
        }

        private static void AddWarning(
            ICollection<GoapExecutorDiagnostic> warnings,
            GoapExecutorDiagnostic diagnostic)
        {
            if (diagnostic.Status == GoapExecutorDiagnosticStatus.Warning)
            {
                warnings.Add(diagnostic);
            }
        }

        private static GoapExecutorDiagnostic CombineWarnings(
            IReadOnlyList<GoapExecutorDiagnostic> warnings)
        {
            return warnings.Count == 0
                ? GoapExecutorDiagnostic.Ready()
                : GoapExecutorDiagnostic.Warning(
                    warnings[0].Code,
                    string.Join("\n", warnings.Select(item => item.Message)));
        }

        private IEnumerator PerformSequence(GoapActionContext context)
        {
            foreach (var step in context.Definition.ExecutionSteps)
            {
                if (step == null)
                {
                    continue;
                }

                yield return PerformStep(step, context);
                if (Status != GoapActionStatus.Running)
                {
                    ReleaseTarget();
                    yield break;
                }
            }

            ReleaseTarget();
            Succeed();
        }

        private IEnumerator PerformStep(GoapActionStep step, GoapActionContext context)
        {
            switch (step.Kind)
            {
                case GoapActionStepKind.FindSmartObject:
                    ReleaseTarget();
                    _targetOwner = context.Agent;
                    _target = ResolveContextSmartObject(context, step.TargetCategory, true) ??
                              GoapSmartObject.FindClosest(
                                  step.TargetCategory,
                                  transform.position,
                                  context.Agent,
                                  float.PositiveInfinity,
                                  true);
                    if (_target == null)
                    {
                        Fail($"No available SmartObject with category '{step.TargetCategory}' was found");
                    }
                    break;

                case GoapActionStepKind.ReserveTarget:
                    if (_target == null)
                    {
                        Fail("Reserve Target has no current SmartObject target");
                        break;
                    }

                    _waitingForReservation = true;
                    var deadline = Time.time + step.Timeout;
                    while (Time.time < deadline && !_target.RequestReservation(context.Agent, step.Timeout))
                    {
                        if (!CanContinue(context))
                        {
                            Fail($"Reservation for '{_target.name}' was invalidated while waiting");
                            break;
                        }

                        yield return null;
                    }

                    _waitingForReservation = false;
                    if (Status == GoapActionStatus.Running && !_target.IsReservedBy(context.Agent))
                    {
                        Fail($"Reservation timeout expired for '{_target.name}' after {step.Timeout:0.##}s");
                    }
                    break;

                case GoapActionStepKind.MoveToTarget:
                    _namedTarget = context.NamedTarget != null
                        ? context.NamedTarget
                        : ResolveNamedTarget(context.Agent, step.TargetId);
                    var destination = _target != null ? _target.transform : _namedTarget;
                    if (destination == null)
                    {
                        Fail(string.IsNullOrWhiteSpace(step.TargetId)
                            ? "Move To Target has no current SmartObject target"
                            : $"Named target '{step.TargetId}' is not assigned in GoapAgentAuthoring");
                        break;
                    }

                    yield return MoveTo(
                        context,
                        destination,
                        step.InteractionRange,
                        step.MoveSpeed,
                        step.UseNavMesh);
                    break;

                case GoapActionStepKind.Interact:
                    if (_target == null || !_target.Interact(context.Agent))
                    {
                        Fail("SmartObject interaction failed because the target is unavailable or reserved");
                    }
                    break;

                case GoapActionStepKind.Wait:
                    yield return WaitWithValidation(context, step.Duration);
                    break;

                case GoapActionStepKind.ConsumeTarget:
                    if (_target == null || !_target.TryUse(context.Agent))
                    {
                        Fail("SmartObject could not be consumed because it is unavailable or reserved");
                    }
                    break;

                case GoapActionStepKind.ReleaseTarget:
                    ReleaseTarget();
                    _targetOwner = context.Agent;
                    break;

                case GoapActionStepKind.InventoryAdd:
                    if (!ApplyInventoryOperation(GoapInventoryOperation.Add, step.ItemId, step.Amount))
                    {
                        Fail($"Could not add {step.Amount} x '{step.ItemId}': GoapInventory is missing");
                    }
                    break;

                case GoapActionStepKind.InventoryRemove:
                    if (!ApplyInventoryOperation(GoapInventoryOperation.Remove, step.ItemId, step.Amount))
                    {
                        var available = TryGetComponent<GoapInventory>(out var inventory)
                            ? inventory.GetAmount(step.ItemId)
                            : 0;
                        Fail($"Could not remove {step.Amount} x '{step.ItemId}': inventory contains {available}");
                    }
                    break;

                case GoapActionStepKind.SetFact:
                case GoapActionStepKind.AddFact:
                case GoapActionStepKind.SubtractFact:
                    ApplyFactStep(step, context);
                    break;

                case GoapActionStepKind.TriggerAnimation:
                    TriggerAnimation(step.EventId);
                    break;

                case GoapActionStepKind.InvokeEvent:
                    if (!TryGetComponent<GoapActionEventReceiver>(out var receiver) || !receiver.Invoke(step.EventId))
                    {
                        Fail($"UnityEvent '{step.EventId}' is not configured on GoapActionEventReceiver");
                    }
                    break;
            }
        }

        private IEnumerator MoveTo(
            GoapActionContext context,
            Transform destination,
            float range,
            float speed,
            bool useNavMesh)
        {
            if (useNavMesh &&
                TryGetComponent<NavMeshAgent>(out var navigation) &&
                navigation.enabled &&
                navigation.isOnNavMesh)
            {
                if (!navigation.SetDestination(destination.position))
                {
                    Fail($"NavMeshAgent rejected destination '{destination.name}'");
                    yield break;
                }

                while (navigation.pathPending)
                {
                    if (!CanContinue(context))
                    {
                        navigation.ResetPath();
                        Fail("Movement was invalidated while calculating a NavMesh path");
                        yield break;
                    }

                    yield return null;
                }

                if (navigation.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    navigation.ResetPath();
                    Fail($"No complete NavMesh path to '{destination.name}'");
                    yield break;
                }

                while (navigation.remainingDistance > range)
                {
                    if (!CanContinue(context))
                    {
                        navigation.ResetPath();
                        Fail("Movement was invalidated before reaching the target");
                        yield break;
                    }

                    yield return null;
                }

                navigation.ResetPath();
                yield break;
            }

            while (destination != null && Vector3.Distance(transform.position, destination.position) > range)
            {
                if (!CanContinue(context))
                {
                    Fail("Direct movement was invalidated before reaching the target");
                    yield break;
                }

                var targetPosition = destination.position;
                targetPosition.y = transform.position.y;
                var direction = targetPosition - transform.position;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction),
                        Time.deltaTime * 8f);
                }

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    speed * Time.deltaTime);
                yield return null;
            }

            if (destination == null)
            {
                Fail("Movement target was destroyed before the Agent arrived");
            }
        }

        private IEnumerator WaitWithValidation(GoapActionContext context, float duration)
        {
            var finishTime = Time.time + duration;
            while (Time.time < finishTime)
            {
                if (!CanContinue(context))
                {
                    Fail("Wait was invalidated by the current scene state");
                    yield break;
                }

                yield return null;
            }
        }

        private void ApplyFactStep(GoapActionStep step, GoapActionContext context)
        {
            var factValue = step.FactValue;
            if (!factValue.IsValid)
            {
                Fail($"{step.Kind} has no configured Fact");
                return;
            }

            var operation = step.Kind switch
            {
                GoapActionStepKind.AddFact => GoapEffectOperation.Add,
                GoapActionStepKind.SubtractFact => GoapEffectOperation.Subtract,
                _ => GoapEffectOperation.Set
            };
            var current = context.GetFactValue(factValue.Fact);
            context.StageFact(factValue.Fact, current.Apply(operation, factValue.Value));
        }

        private bool CanApplyInventory(GoapInventoryOperation operation, string itemId, int amount)
        {
            if (operation != GoapInventoryOperation.Remove)
            {
                return true;
            }

            var inventory = GetComponent<GoapInventory>();
            return inventory != null && inventory.GetAmount(itemId) >= amount;
        }

        private bool ApplyInventoryOperation(GoapInventoryOperation operation, string itemId, int amount)
        {
            if (operation == GoapInventoryOperation.None)
            {
                return true;
            }

            var inventory = GetComponent<GoapInventory>();
            if (inventory == null)
            {
                return false;
            }

            if (operation == GoapInventoryOperation.Add)
            {
                inventory.Add(itemId, amount);
                return true;
            }

            return inventory.Remove(itemId, amount);
        }

        private void TriggerAnimation(string triggerName)
        {
            if (!string.IsNullOrWhiteSpace(triggerName) && TryGetComponent<Animator>(out var animator))
            {
                animator.SetTrigger(triggerName);
            }
        }

        private static Transform ResolveNamedTarget(GoapAgent agent, string targetId)
        {
            return agent.TryGetComponent<GoapAgentAuthoring>(out var authoring)
                ? authoring.ResolveTarget(targetId)
                : null;
        }

        private static GoapSmartObject ResolveContextSmartObject(
            GoapActionContext context,
            string category,
            bool includeBusy)
        {
            var target = context?.SmartObjectTarget;
            if (target == null ||
                !string.Equals(target.Category, category, System.StringComparison.Ordinal) ||
                (includeBusy ? !target.Available : !target.IsAvailableTo(context.Agent)))
            {
                return null;
            }

            return target;
        }

        private void StopNavigation()
        {
            if (TryGetComponent<NavMeshAgent>(out var navigation) && navigation.enabled && navigation.isOnNavMesh)
            {
                navigation.ResetPath();
            }
        }

        private void FailAndRelease(string reason)
        {
            Fail(reason);
            ReleaseTarget();
        }

        private void ReleaseTarget()
        {
            if (_target != null)
            {
                _target.Release(_targetOwner);
            }

            _target = null;
            _namedTarget = null;
            _targetOwner = null;
            _waitingForReservation = false;
        }
    }
}
