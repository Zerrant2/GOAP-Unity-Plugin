using System.Collections;
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
                return CanStartSequence(context);
            }

            if (_settings.Mode == GoapExecutionMode.SmartObjectInteraction)
            {
                if (string.IsNullOrWhiteSpace(_settings.TargetCategory))
                {
                    return false;
                }

                _target = GoapSmartObject.FindClosest(
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
                FailAndRelease();
                yield break;
            }

            if (!ApplyInventoryOperation(
                    _settings.InventoryOperation,
                    _settings.InventoryItemId,
                    _settings.InventoryAmount))
            {
                FailAndRelease();
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

        private bool CanStartSequence(GoapActionContext context)
        {
            var inventory = GetComponent<GoapInventory>();
            foreach (var step in context.Definition.ExecutionSteps)
            {
                if (step == null || step.Kind != GoapActionStepKind.InventoryRemove)
                {
                    continue;
                }

                if (inventory == null || inventory.GetAmount(step.ItemId) < step.Amount)
                {
                    return false;
                }
            }

            return true;
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
                    _target = GoapSmartObject.FindClosest(
                        step.TargetCategory,
                        transform.position,
                        context.Agent,
                        float.PositiveInfinity,
                        true);
                    if (_target == null)
                    {
                        Fail();
                    }
                    break;

                case GoapActionStepKind.ReserveTarget:
                    if (_target == null)
                    {
                        Fail();
                        break;
                    }

                    _waitingForReservation = true;
                    var deadline = Time.time + step.Timeout;
                    while (Time.time < deadline && !_target.RequestReservation(context.Agent, step.Timeout))
                    {
                        if (!CanContinue(context))
                        {
                            Fail();
                            break;
                        }

                        yield return null;
                    }

                    _waitingForReservation = false;
                    if (Status == GoapActionStatus.Running && !_target.IsReservedBy(context.Agent))
                    {
                        Fail();
                    }
                    break;

                case GoapActionStepKind.MoveToTarget:
                    _namedTarget = ResolveNamedTarget(context.Agent, step.TargetId);
                    var destination = _target != null ? _target.transform : _namedTarget;
                    if (destination == null)
                    {
                        Fail();
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
                        Fail();
                    }
                    break;

                case GoapActionStepKind.Wait:
                    yield return WaitWithValidation(context, step.Duration);
                    break;

                case GoapActionStepKind.ConsumeTarget:
                    if (_target == null || !_target.TryUse(context.Agent))
                    {
                        Fail();
                    }
                    break;

                case GoapActionStepKind.ReleaseTarget:
                    ReleaseTarget();
                    _targetOwner = context.Agent;
                    break;

                case GoapActionStepKind.InventoryAdd:
                    if (!ApplyInventoryOperation(GoapInventoryOperation.Add, step.ItemId, step.Amount))
                    {
                        Fail();
                    }
                    break;

                case GoapActionStepKind.InventoryRemove:
                    if (!ApplyInventoryOperation(GoapInventoryOperation.Remove, step.ItemId, step.Amount))
                    {
                        Fail();
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
                        Fail();
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
                navigation.SetDestination(destination.position);
                while (navigation.pathPending || navigation.remainingDistance > range)
                {
                    if (!CanContinue(context))
                    {
                        navigation.ResetPath();
                        Fail();
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
                    Fail();
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
        }

        private IEnumerator WaitWithValidation(GoapActionContext context, float duration)
        {
            var finishTime = Time.time + duration;
            while (Time.time < finishTime)
            {
                if (!CanContinue(context))
                {
                    Fail();
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
                Fail();
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

        private void StopNavigation()
        {
            if (TryGetComponent<NavMeshAgent>(out var navigation) && navigation.enabled && navigation.isOnNavMesh)
            {
                navigation.ResetPath();
            }
        }

        private void FailAndRelease()
        {
            Fail();
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
