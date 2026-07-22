using System.Collections;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OutpostAgent))]
    public sealed class OutpostActionBehaviour : GoapActionBehaviour
    {
        [SerializeField, Min(0.1f)] private float _moveSpeed = 3.6f;
        [SerializeField, Min(0.1f)] private float _interactionRange = 1.05f;

        private OutpostAgent _actor;
        private Transform _target;
        private GoapSmartObject _reservedTarget;
        private OutpostMonster _monster;
        private Vector3 _pointTarget;
        private bool _usesPointTarget;

        public override bool Supports(GoapActionDefinition action)
        {
            return action != null && action.ExecutorId.StartsWith("outpost.");
        }

        public override GoapExecutorDiagnostic EvaluateStart(GoapActionContext context)
        {
            var diagnostic = base.EvaluateStart(context);
            if (!diagnostic.CanStart)
            {
                return diagnostic;
            }

            var actor = GetComponent<OutpostAgent>();
            if (actor == null || actor.Controller == null)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.RequiredComponentMissing,
                    "OutpostAgent or OutpostGameController is missing");
            }

            return HasTargetFor(context.Definition.ExecutorId, actor)
                ? GoapExecutorDiagnostic.Ready()
                : GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.TargetMissing,
                    $"No scene target is currently available for '{context.Definition.DisplayName}'");
        }

        public override bool CanStart(GoapActionContext context)
        {
            ReleaseTarget(context.Agent);
            _actor = GetComponent<OutpostAgent>();
            if (_actor == null || _actor.Controller == null || !base.CanStart(context))
            {
                return false;
            }

            return ResolveAndReserve(
                context.Definition.ExecutorId,
                context.Agent,
                context.SmartObjectTarget);
        }

        public override bool CanContinue(GoapActionContext context)
        {
            if (!base.CanContinue(context))
            {
                return false;
            }

            if (_monster != null)
            {
                return _monster.IsAlive && _monster.SmartObject.IsReservedBy(context.Agent);
            }

            return _reservedTarget == null ||
                   (_reservedTarget.Available && _reservedTarget.IsReservedBy(context.Agent));
        }

        protected override IEnumerator Perform(GoapActionContext context)
        {
            _actor.SetActionProgress(0f);
            var actionId = context.Definition.ExecutorId;
            if (_target != null || _usesPointTarget)
            {
                yield return MoveToTarget(context);
                if (Status != GoapActionStatus.Running)
                {
                    ReleaseTarget(context.Agent);
                    yield break;
                }
            }

            var duration = GetWorkDuration(actionId);
            if (duration > 0f)
            {
                yield return Work(context, duration);
                if (Status != GoapActionStatus.Running)
                {
                    ReleaseTarget(context.Agent);
                    yield break;
                }
            }

            var applied = ApplyPhysicalEffect(actionId, context.Agent);
            ReleaseTarget(context.Agent);
            _actor.SetActionProgress(0f);
            if (applied)
            {
                Succeed();
            }
            else
            {
                Fail($"World state changed before '{context.Definition.DisplayName}' completed");
            }
        }

        protected override void OnCancelled(GoapActionContext context)
        {
            _actor?.SetActionProgress(0f);
            ReleaseTarget(context?.Agent);
        }

        private bool ResolveAndReserve(
            string actionId,
            GoapAgent agent,
            GoapSmartObject plannedTarget)
        {
            var controller = _actor.Controller;
            switch (actionId)
            {
                case OutpostIds.Eat:
                case OutpostIds.DeliverWood:
                case OutpostIds.DeliverFood:
                    _target = controller.Stockpile != null ? controller.Stockpile.transform : null;
                    return _target != null;
                case OutpostIds.Repair:
                    _target = controller.Camp != null ? controller.Camp.transform : null;
                    return _target != null && controller.Stockpile.Wood > 0;
                case OutpostIds.Sleep:
                    return ReservePlannedOrClosest(plannedTarget, OutpostIds.BedCategory, agent);
                case OutpostIds.HarvestWood:
                    return ReservePlannedOrClosest(plannedTarget, OutpostIds.TreeCategory, agent);
                case OutpostIds.HarvestFood:
                    return ReservePlannedOrClosest(plannedTarget, OutpostIds.FoodCategory, agent);
                case OutpostIds.TakeWeapon:
                    return Reserve(plannedTarget != null ? plannedTarget : controller.Armory, agent);
                case OutpostIds.Attack:
                    _monster = plannedTarget != null ? plannedTarget.GetComponent<OutpostMonster>() : null;
                    _monster = _monster != null ? _monster : controller.FindClosestMonster(transform.position, agent);
                    if (_monster == null || !Reserve(_monster.SmartObject, agent))
                    {
                        _monster = null;
                        return false;
                    }

                    return true;
                case OutpostIds.Flee:
                    _target = controller.SafePoint;
                    return _target != null;
                case OutpostIds.Patrol:
                    _pointTarget = controller.GetPatrolPoint(_actor.Index);
                    _usesPointTarget = true;
                    return true;
                default:
                    return false;
            }
        }

        private bool HasTargetFor(string actionId, OutpostAgent actor)
        {
            var controller = actor.Controller;
            return actionId switch
            {
                OutpostIds.Eat => controller.Stockpile != null && controller.Stockpile.Food > 0,
                OutpostIds.DeliverWood or OutpostIds.DeliverFood => controller.Stockpile != null,
                OutpostIds.Repair => controller.Camp != null && controller.Stockpile != null && controller.Stockpile.Wood > 0,
                OutpostIds.Sleep => GoapSmartObject.FindClosest(OutpostIds.BedCategory, transform.position, actor.Agent) != null,
                OutpostIds.HarvestWood => GoapSmartObject.FindClosest(OutpostIds.TreeCategory, transform.position, actor.Agent) != null,
                OutpostIds.HarvestFood => GoapSmartObject.FindClosest(OutpostIds.FoodCategory, transform.position, actor.Agent) != null,
                OutpostIds.TakeWeapon => controller.Armory != null && controller.Armory.IsAvailableTo(actor.Agent),
                OutpostIds.Attack => controller.FindClosestMonster(transform.position, actor.Agent) != null,
                OutpostIds.Flee => controller.SafePoint != null,
                OutpostIds.Patrol => true,
                _ => false
            };
        }

        private bool ReserveClosest(string category, GoapAgent agent)
        {
            var target = GoapSmartObject.FindClosest(category, transform.position, agent);
            return Reserve(target, agent);
        }

        private bool ReservePlannedOrClosest(
            GoapSmartObject plannedTarget,
            string category,
            GoapAgent agent)
        {
            if (plannedTarget != null &&
                plannedTarget.Category == category &&
                Reserve(plannedTarget, agent))
            {
                return true;
            }

            return ReserveClosest(category, agent);
        }

        private bool Reserve(GoapSmartObject target, GoapAgent agent)
        {
            if (target == null || !target.TryReserve(agent))
            {
                return false;
            }

            _reservedTarget = target;
            _target = target.transform;
            return true;
        }

        private IEnumerator MoveToTarget(GoapActionContext context)
        {
            while (Status == GoapActionStatus.Running)
            {
                if (!_usesPointTarget && _target == null)
                {
                    Fail("Movement target was removed");
                    yield break;
                }

                var destination = _usesPointTarget ? _pointTarget : _target.position;
                destination.y = transform.position.y;
                var distance = Vector3.Distance(transform.position, destination);
                if (distance <= _interactionRange)
                {
                    yield break;
                }

                if (_reservedTarget != null && !_reservedTarget.RefreshReservation(context.Agent))
                {
                    Fail("Target reservation expired while moving");
                    yield break;
                }

                var direction = destination - transform.position;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction),
                        Time.deltaTime * 9f);
                }

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    destination,
                    _moveSpeed * Time.deltaTime);
                yield return null;
            }
        }

        private IEnumerator Work(GoapActionContext context, float duration)
        {
            var elapsed = 0f;
            while (Status == GoapActionStatus.Running && elapsed < duration)
            {
                if (_reservedTarget != null && !_reservedTarget.RefreshReservation(context.Agent))
                {
                    Fail("Target reservation expired during interaction");
                    yield break;
                }

                elapsed += Time.deltaTime;
                _actor.SetActionProgress(elapsed / duration);
                yield return null;
            }
        }

        private bool ApplyPhysicalEffect(string actionId, GoapAgent agent)
        {
            switch (actionId)
            {
                case OutpostIds.Eat:
                    return _actor.Eat();
                case OutpostIds.Sleep:
                    _actor.Rest();
                    return true;
                case OutpostIds.HarvestWood:
                    return Harvest(OutpostResourceKind.Wood, agent);
                case OutpostIds.DeliverWood:
                    return _actor.Deliver(OutpostResourceKind.Wood);
                case OutpostIds.HarvestFood:
                    return Harvest(OutpostResourceKind.Food, agent);
                case OutpostIds.DeliverFood:
                    return _actor.Deliver(OutpostResourceKind.Food);
                case OutpostIds.TakeWeapon:
                    _actor.EquipWeapon();
                    return true;
                case OutpostIds.Attack:
                    if (_monster == null || !_monster.IsAlive)
                    {
                        return false;
                    }

                    _monster.Damage(_monster.MaxHealth);
                    _actor.MarkEnemyDefeated();
                    return true;
                case OutpostIds.Repair:
                    if (!_actor.Controller.Stockpile.TryTake(OutpostResourceKind.Wood, 1))
                    {
                        return false;
                    }

                    _actor.Controller.Camp.Repair(48f);
                    _actor.MarkCampRepaired();
                    return true;
                case OutpostIds.Flee:
                    _actor.MarkSafe(true);
                    return true;
                case OutpostIds.Patrol:
                    _actor.MarkPatrolDone();
                    return true;
                default:
                    return false;
            }
        }

        private bool Harvest(OutpostResourceKind kind, GoapAgent agent)
        {
            var node = _target != null ? _target.GetComponent<OutpostResourceNode>() : null;
            if (node == null || node.Kind != kind || !node.TryHarvest(agent))
            {
                return false;
            }

            _actor.AddCarried(kind, 1);
            return true;
        }

        private void ReleaseTarget(GoapAgent agent)
        {
            if (_reservedTarget != null && agent != null)
            {
                _reservedTarget.Release(agent);
            }

            _reservedTarget = null;
            _monster = null;
            _target = null;
            _usesPointTarget = false;
        }

        private static float GetWorkDuration(string actionId)
        {
            return actionId switch
            {
                OutpostIds.Sleep => 2.4f,
                OutpostIds.HarvestWood => 1.25f,
                OutpostIds.HarvestFood => 0.9f,
                OutpostIds.Attack => 0.75f,
                OutpostIds.Repair => 1.4f,
                OutpostIds.Eat => 0.55f,
                OutpostIds.TakeWeapon => 0.45f,
                _ => 0.2f
            };
        }
    }
}
