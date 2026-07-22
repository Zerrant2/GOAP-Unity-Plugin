using System.Collections;
using UnityEngine;

namespace Practice.GOAP.Demo
{
    public enum DemoActionEffect
    {
        None,
        TakeFood,
        Eat,
        Rest,
        TakeWeapon,
        DefeatEnemy
    }

    public sealed class DemoActionBehaviour : GoapActionBehaviour
    {
        [SerializeField] private DemoWorldObjectType _targetType;
        [SerializeField] private DemoActionEffect _effect;
        [SerializeField, Min(0.1f)] private float _moveSpeed = 3.5f;
        [SerializeField, Min(0f)] private float _workDuration = 0.6f;
        [SerializeField, Min(0.1f)] private float _interactionRange = 1.1f;

        private DemoWorldObject _target;

        public void Configure(
            string executorId,
            DemoWorldObjectType targetType,
            DemoActionEffect effect,
            float workDuration = 0.6f)
        {
            SetExecutorId(executorId);
            _targetType = targetType;
            _effect = effect;
            _workDuration = Mathf.Max(0f, workDuration);
        }

        public override bool CanStart(GoapActionContext context)
        {
            if (!base.CanStart(context))
            {
                return false;
            }

            _target = _targetType == DemoWorldObjectType.None
                ? null
                : DemoWorldObject.FindClosest(_targetType, transform.position);
            return _targetType == DemoWorldObjectType.None || _target != null;
        }

        public override GoapExecutorDiagnostic EvaluateStart(GoapActionContext context)
        {
            var baseDiagnostic = base.EvaluateStart(context);
            if (!baseDiagnostic.CanStart)
            {
                return baseDiagnostic;
            }

            if (!TryGetComponent<DemoAgentState>(out _))
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.RequiredComponentMissing,
                    "DemoAgentState component is missing");
            }

            if (_targetType != DemoWorldObjectType.None &&
                DemoWorldObject.FindClosest(_targetType, transform.position) == null)
            {
                return GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.TargetMissing,
                    $"No available demo target of type '{_targetType}' was found");
            }

            return GoapExecutorDiagnostic.Ready();
        }

        public override bool CanContinue(GoapActionContext context)
        {
            return base.CanContinue(context) &&
                   (_targetType == DemoWorldObjectType.None || (_target != null && _target.Available));
        }

        protected override IEnumerator Perform(GoapActionContext context)
        {
            if (_target != null)
            {
                while (_target != null && _target.Available &&
                       Vector3.Distance(transform.position, _target.transform.position) > _interactionRange)
                {
                    var targetPosition = _target.transform.position;
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
                        _moveSpeed * Time.deltaTime);
                    yield return null;
                }

                if (_target == null || !_target.Available)
                {
                    Fail($"Demo target '{_targetType}' became unavailable while moving");
                    yield break;
                }
            }

            if (_workDuration > 0f)
            {
                yield return new WaitForSeconds(_workDuration);
            }

            var agentState = GetComponent<DemoAgentState>();
            if (agentState == null || !ApplyEffect(agentState))
            {
                Fail(agentState == null
                    ? "DemoAgentState component is missing"
                    : $"Demo effect '{_effect}' could not be applied");
                yield break;
            }

            Succeed();
        }

        protected override void OnCancelled(GoapActionContext context)
        {
            _target = null;
        }

        private bool ApplyEffect(DemoAgentState agentState)
        {
            switch (_effect)
            {
                case DemoActionEffect.TakeFood:
                    if (_target == null || !_target.TryUse()) return false;
                    agentState.TakeFood();
                    return true;
                case DemoActionEffect.Eat:
                    return agentState.Eat();
                case DemoActionEffect.Rest:
                    if (_target == null || !_target.TryUse()) return false;
                    agentState.Rest();
                    return true;
                case DemoActionEffect.TakeWeapon:
                    if (_target == null || !_target.TryUse()) return false;
                    agentState.TakeWeapon();
                    return true;
                case DemoActionEffect.DefeatEnemy:
                    return _target != null && _target.TryUse();
                case DemoActionEffect.None:
                    return true;
                default:
                    return false;
            }
        }
    }
}
