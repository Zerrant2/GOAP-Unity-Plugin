using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GoapAgent))]
    public sealed class OutpostAgent : MonoBehaviour
    {
        [SerializeField] private OutpostGameController _controller;
        [SerializeField] private OutpostRole _role;
        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField, Min(0)] private int _index;
        [SerializeField, Range(0f, 100f)] private float _hunger = 12f;
        [SerializeField, Range(0f, 100f)] private float _energy = 100f;
        [SerializeField, Range(0f, 100f)] private float _health = 100f;
        [SerializeField, Min(0.01f)] private float _hungerRate = 1.25f;
        [SerializeField, Min(0.01f)] private float _energyDrainRate = 0.75f;

        private GoapAgent _agent;
        private Renderer _renderer;
        private bool _subscribed;
        private bool _selected;

        public OutpostGameController Controller => _controller;
        public GoapAgent Agent => _agent != null ? _agent : _agent = GetComponent<GoapAgent>();
        public OutpostRole Role => _role;
        public int Index => _index;
        public float Hunger => _hunger;
        public float Energy => _energy;
        public float Health => _health;
        public bool IsAlive => _health > 0f;
        public int CarryWood { get; private set; }
        public int CarryFood { get; private set; }
        public bool HasWeapon { get; private set; }
        public bool IsSafe { get; private set; } = true;
        public bool WoodDelivered { get; private set; }
        public bool FoodDelivered { get; private set; }
        public bool CampRepaired { get; private set; }
        public bool EnemyDefeated { get; private set; }
        public bool PatrolDone { get; private set; }
        public float ActionProgress { get; private set; }

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            Subscribe();
        }

        private void Start()
        {
            Subscribe();
            if (_profile != null)
            {
                Agent.Configure(_profile);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && _agent != null)
            {
                _agent.GoalCompleted -= OnGoalCompleted;
            }
        }

        private void Update()
        {
            if (!IsAlive || _controller == null || _controller.IsGameOver)
            {
                return;
            }

            _hunger = Mathf.Min(100f, _hunger + _hungerRate * Time.deltaTime);
            _energy = Mathf.Max(0f, _energy - _energyDrainRate * Time.deltaTime);
            if (_hunger >= 99f)
            {
                Damage(4f * Time.deltaTime);
            }
        }

        public void ConfigureAuthored(
            OutpostGameController controller,
            OutpostRole role,
            GoapAgentProfile profile,
            Material material,
            int index)
        {
            _controller = controller;
            _index = index;
            _hunger = 8f + index * 7f;
            _energy = Mathf.Clamp(96f - index * 8f, 55f, 100f);
            SetRole(role, profile, material);
            Subscribe();
        }

        public void Bind(OutpostGameController controller)
        {
            _controller = controller;
            Subscribe();
        }

        public void SetRole(OutpostRole role, GoapAgentProfile profile, Material material)
        {
            _role = role;
            _profile = profile;
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }

            if (_renderer != null && material != null)
            {
                _renderer.sharedMaterial = material;
            }

            if (profile != null)
            {
                Agent.Configure(profile);
                Agent.ForceDecision();
            }
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
        }

        public void SetActionProgress(float value)
        {
            ActionProgress = Mathf.Clamp01(value);
        }

        public void AddCarried(OutpostResourceKind kind, int amount)
        {
            if (kind == OutpostResourceKind.Wood)
            {
                CarryWood = Mathf.Max(0, CarryWood + amount);
            }
            else
            {
                CarryFood = Mathf.Max(0, CarryFood + amount);
            }
        }

        public bool Deliver(OutpostResourceKind kind)
        {
            var amount = kind == OutpostResourceKind.Wood ? CarryWood : CarryFood;
            if (amount <= 0 || _controller == null || _controller.Stockpile == null)
            {
                return false;
            }

            _controller.Stockpile.Add(kind, amount);
            if (kind == OutpostResourceKind.Wood)
            {
                CarryWood = 0;
                WoodDelivered = true;
            }
            else
            {
                CarryFood = 0;
                FoodDelivered = true;
            }

            return true;
        }

        public bool Eat()
        {
            if (_controller?.Stockpile == null ||
                !_controller.Stockpile.TryTake(OutpostResourceKind.Food, 1))
            {
                return false;
            }

            _hunger = 8f;
            return true;
        }

        public void Rest()
        {
            _energy = 100f;
        }

        public void EquipWeapon()
        {
            HasWeapon = true;
        }

        public void MarkSafe(bool value)
        {
            IsSafe = value;
        }

        public void MarkEnemyDefeated()
        {
            EnemyDefeated = true;
        }

        public void MarkCampRepaired()
        {
            CampRepaired = true;
        }

        public void MarkPatrolDone()
        {
            PatrolDone = true;
        }

        public void Damage(float amount)
        {
            if (!IsAlive)
            {
                return;
            }

            _health = Mathf.Max(0f, _health - Mathf.Max(0f, amount));
            if (_health <= 0f)
            {
                Agent.enabled = false;
                _controller?.NotifyAgentDied(this);
                gameObject.SetActive(false);
            }
        }

        private void Subscribe()
        {
            if (_subscribed || Agent == null)
            {
                return;
            }

            Agent.GoalCompleted += OnGoalCompleted;
            _subscribed = true;
        }

        private void OnGoalCompleted(GoapAgent agent, GoapGoalDefinition goal)
        {
            switch (goal.DisplayName)
            {
                case "Collect Wood":
                    WoodDelivered = false;
                    break;
                case "Collect Food":
                    FoodDelivered = false;
                    break;
                case "Repair Camp":
                    CampRepaired = false;
                    break;
                case "Defend Outpost":
                    EnemyDefeated = false;
                    break;
                case "Patrol":
                    PatrolDone = false;
                    break;
            }

            Agent.ForceDecision();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_selected)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.85f);
        }
    }
}
