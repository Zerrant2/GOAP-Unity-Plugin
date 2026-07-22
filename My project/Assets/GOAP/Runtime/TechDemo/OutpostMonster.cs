using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostMonster : MonoBehaviour
    {
        [SerializeField] private OutpostGameController _controller;
        [SerializeField] private OutpostCamp _camp;
        [SerializeField] private GoapSmartObject _smartObject;
        [SerializeField, Min(1f)] private float _maxHealth = 60f;
        [SerializeField, Min(0f)] private float _health = 60f;
        [SerializeField, Min(0.1f)] private float _moveSpeed = 1.7f;
        [SerializeField, Min(0.1f)] private float _attackInterval = 0.9f;
        [SerializeField, Min(0.1f)] private float _attackRange = 1.35f;
        [SerializeField, Min(0f)] private float _damage = 9f;

        private float _nextAttackAt;

        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _health > 0f && isActiveAndEnabled;
        public GoapSmartObject SmartObject => _smartObject;

        public void Configure(
            OutpostGameController controller,
            OutpostCamp camp,
            GoapSmartObject smartObject,
            float health,
            float moveSpeed)
        {
            _controller = controller;
            _camp = camp;
            _smartObject = smartObject;
            _maxHealth = Mathf.Max(1f, health);
            _health = _maxHealth;
            _moveSpeed = Mathf.Max(0.1f, moveSpeed);
        }

        private void Update()
        {
            if (!IsAlive || _controller == null || _controller.IsGameOver || _camp == null)
            {
                return;
            }

            var targetAgent = FindNearbyAgent(3.2f);
            var targetPosition = targetAgent != null ? targetAgent.transform.position : _camp.transform.position;
            targetPosition.y = transform.position.y;
            var distance = Vector3.Distance(transform.position, targetPosition);
            if (distance > _attackRange)
            {
                var direction = targetPosition - transform.position;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction),
                        Time.deltaTime * 7f);
                }

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    _moveSpeed * Time.deltaTime);
                return;
            }

            if (Time.time < _nextAttackAt)
            {
                return;
            }

            _nextAttackAt = Time.time + _attackInterval;
            if (targetAgent != null)
            {
                targetAgent.Damage(_damage);
            }
            else
            {
                _camp.Damage(_damage);
            }
        }

        public bool IsAvailableTo(GoapAgent agent)
        {
            return IsAlive && _smartObject != null && _smartObject.IsAvailableTo(agent);
        }

        public void Damage(float amount)
        {
            if (!IsAlive)
            {
                return;
            }

            _health = Mathf.Max(0f, _health - Mathf.Max(0f, amount));
            if (_health > 0f)
            {
                return;
            }

            _smartObject?.SetAvailable(false);
            _controller?.NotifyMonsterDefeated(this);
            Destroy(gameObject, 0.15f);
        }

        private OutpostAgent FindNearbyAgent(float range)
        {
            OutpostAgent closest = null;
            var bestDistance = range * range;
            foreach (var agent in _controller.Agents)
            {
                if (agent == null || !agent.IsAlive)
                {
                    continue;
                }

                var distance = (agent.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = agent;
                }
            }

            return closest;
        }
    }
}
