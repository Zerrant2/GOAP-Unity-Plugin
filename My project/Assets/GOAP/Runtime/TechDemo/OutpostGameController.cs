using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostGameController : MonoBehaviour
    {
        [SerializeField] private OutpostStockpile _stockpile;
        [SerializeField] private OutpostCamp _camp;
        [SerializeField] private Transform _safePoint;
        [SerializeField] private Transform _agentSpawnPoint;
        [SerializeField] private Transform _monsterSpawnPoint;
        [SerializeField] private GoapSmartObject _armory;
        [SerializeField] private List<OutpostRoleProfile> _roleProfiles = new();
        [SerializeField] private List<OutpostAgent> _agents = new();
        [SerializeField] private Material _monsterMaterial;
        [SerializeField, Min(1)] private int _woodTarget = 10;
        [SerializeField, Min(1)] private int _foodTarget = 12;
        [SerializeField, Min(0)] private int _recruitFoodCost = 3;
        [SerializeField, Min(3f)] private float _firstWaveDelay = 14f;
        [SerializeField, Min(8f)] private float _waveInterval = 32f;

        private readonly List<OutpostMonster> _monsters = new();
        private readonly List<string> _eventLog = new();
        private float _nextWaveAt;
        private int _wave;
        private int _nextAgentIndex;

        public static OutpostGameController Instance { get; private set; }
        public OutpostStockpile Stockpile => _stockpile;
        public OutpostCamp Camp => _camp;
        public Transform SafePoint => _safePoint;
        public GoapSmartObject Armory => _armory;
        public IReadOnlyList<OutpostAgent> Agents => _agents;
        public IReadOnlyList<OutpostMonster> Monsters => _monsters;
        public IReadOnlyList<string> EventLog => _eventLog;
        public int WoodTarget => _woodTarget;
        public int FoodTarget => _foodTarget;
        public int RecruitFoodCost => _recruitFoodCost;
        public int Wave => _wave;
        public float TimeUntilNextWave => Mathf.Max(0f, _nextWaveAt - Time.time);
        public bool HasLivingMonsters => _monsters.Any(monster => monster != null && monster.IsAlive);
        public bool IsGameOver => _camp == null || _camp.IsDestroyed || _agents.All(agent => agent == null || !agent.IsAlive);

        private void Awake()
        {
            Instance = this;
            _agents.RemoveAll(agent => agent == null);
            foreach (var agent in _agents)
            {
                agent.Bind(this);
                _nextAgentIndex = Mathf.Max(_nextAgentIndex, agent.Index + 1);
            }
        }

        private void Start()
        {
            _nextWaveAt = Time.time + _firstWaveDelay;
            AddEvent("Outpost simulation started");
        }

        private void Update()
        {
            _monsters.RemoveAll(monster => monster == null);
            if (IsGameOver || Time.time < _nextWaveAt)
            {
                return;
            }

            SpawnWave();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Configure(
            OutpostStockpile stockpile,
            OutpostCamp camp,
            Transform safePoint,
            Transform agentSpawnPoint,
            Transform monsterSpawnPoint,
            GoapSmartObject armory,
            IEnumerable<OutpostRoleProfile> roleProfiles,
            IEnumerable<OutpostAgent> agents,
            Material monsterMaterial)
        {
            _stockpile = stockpile;
            _camp = camp;
            _safePoint = safePoint;
            _agentSpawnPoint = agentSpawnPoint;
            _monsterSpawnPoint = monsterSpawnPoint;
            _armory = armory;
            _roleProfiles = roleProfiles == null
                ? new List<OutpostRoleProfile>()
                : new List<OutpostRoleProfile>(roleProfiles);
            _agents = agents == null ? new List<OutpostAgent>() : new List<OutpostAgent>(agents);
            _monsterMaterial = monsterMaterial;
        }

        public GoapAgentProfile GetProfile(OutpostRole role)
        {
            return _roleProfiles.FirstOrDefault(item => item.Role == role).Profile;
        }

        public Material GetRoleMaterial(OutpostRole role)
        {
            return _roleProfiles.FirstOrDefault(item => item.Role == role).Material;
        }

        public bool TryRecruit(OutpostRole role)
        {
            if (IsGameOver || GetProfile(role) == null ||
                (_recruitFoodCost > 0 && !_stockpile.TryTake(OutpostResourceKind.Food, _recruitFoodCost)))
            {
                AddEvent("Recruitment failed: more food is required");
                return false;
            }

            var agent = CreateRuntimeAgent(role);
            _agents.Add(agent);
            AddEvent($"Recruited {FormatRole(role)} #{agent.Index + 1}");
            ForceAllAgentsToDecide();
            return true;
        }

        public bool ChangeRole(OutpostAgent agent, OutpostRole role)
        {
            var profile = GetProfile(role);
            if (agent == null || !agent.IsAlive || profile == null)
            {
                return false;
            }

            agent.SetRole(role, profile, GetRoleMaterial(role));
            AddEvent($"Agent #{agent.Index + 1} is now {FormatRole(role)}");
            return true;
        }

        public void SpawnWave()
        {
            if (IsGameOver)
            {
                return;
            }

            _wave++;
            var count = Mathf.Clamp(1 + _wave, 2, 8);
            for (var index = 0; index < count; index++)
            {
                CreateMonster(index, count);
            }

            _nextWaveAt = Time.time + _waveInterval;
            AddEvent($"Wave {_wave}: {count} monsters approaching");
            ForceAllAgentsToDecide();
        }

        public OutpostMonster FindClosestMonster(Vector3 origin, GoapAgent agent = null)
        {
            OutpostMonster closest = null;
            var bestDistance = float.PositiveInfinity;
            foreach (var monster in _monsters)
            {
                if (monster == null || !monster.IsAlive || !monster.IsAvailableTo(agent))
                {
                    continue;
                }

                var distance = (monster.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = monster;
                }
            }

            return closest;
        }

        public bool IsPositionThreatened(Vector3 position, float radius)
        {
            var radiusSquared = radius * radius;
            return _monsters.Any(monster =>
                monster != null && monster.IsAlive &&
                (monster.transform.position - position).sqrMagnitude <= radiusSquared);
        }

        public Vector3 GetPatrolPoint(int seed)
        {
            var angle = (seed * 97f + Time.time * 31f) * Mathf.Deg2Rad;
            var center = _camp != null ? _camp.transform.position : Vector3.zero;
            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 6.5f;
        }

        public void NotifyMonsterDefeated(OutpostMonster monster)
        {
            if (monster != null)
            {
                _monsters.Remove(monster);
                AddEvent("A monster was defeated");
                ForceAllAgentsToDecide();
            }
        }

        public void NotifyAgentDied(OutpostAgent agent)
        {
            if (agent != null)
            {
                AddEvent($"Agent #{agent.Index + 1} was lost");
            }
        }

        public void AddEvent(string message)
        {
            _eventLog.Insert(0, $"{Time.time:000.0}s  {message}");
            if (_eventLog.Count > 8)
            {
                _eventLog.RemoveAt(_eventLog.Count - 1);
            }
        }

        public static string FormatRole(OutpostRole role)
        {
            return role switch
            {
                OutpostRole.Lumberjack => "Lumberjack",
                OutpostRole.Forager => "Forager",
                OutpostRole.Guard => "Guard",
                OutpostRole.Builder => "Builder",
                _ => role.ToString()
            };
        }

        private OutpostAgent CreateRuntimeAgent(OutpostRole role)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            gameObject.name = $"Agent {_nextAgentIndex + 1} - {FormatRole(role)}";
            var offset = new Vector3((_nextAgentIndex % 3 - 1) * 1.2f, 1f, (_nextAgentIndex % 2) * 1.1f);
            gameObject.transform.position = (_agentSpawnPoint != null ? _agentSpawnPoint.position : Vector3.zero) + offset;
            gameObject.transform.localScale = new Vector3(0.72f, 0.9f, 0.72f);
            gameObject.transform.SetParent(transform);

            gameObject.AddComponent<GoapAgent>();
            var actor = gameObject.AddComponent<OutpostAgent>();
            gameObject.AddComponent<OutpostSensor>();
            gameObject.AddComponent<OutpostActionBehaviour>();
            actor.ConfigureAuthored(this, role, GetProfile(role), GetRoleMaterial(role), _nextAgentIndex++);
            gameObject.AddComponent<OutpostAgentLabel>().Configure(actor);
            return actor;
        }

        private void CreateMonster(int index, int count)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            gameObject.name = $"Monster W{_wave}-{index + 1}";
            var center = _monsterSpawnPoint != null ? _monsterSpawnPoint.position : new Vector3(0f, 1f, 12f);
            var spread = (index - (count - 1) * 0.5f) * 1.8f;
            gameObject.transform.position = center + new Vector3(spread, 1f, UnityEngine.Random.Range(-1.2f, 1.2f));
            gameObject.transform.localScale = new Vector3(0.9f, 1.05f, 0.9f);
            gameObject.transform.SetParent(transform);
            if (_monsterMaterial != null)
            {
                gameObject.GetComponent<Renderer>().sharedMaterial = _monsterMaterial;
            }

            var smartObject = gameObject.AddComponent<GoapSmartObject>();
            smartObject.Configure(OutpostIds.EnemyCategory, false);
            var monster = gameObject.AddComponent<OutpostMonster>();
            monster.Configure(this, _camp, smartObject, 55f + _wave * 5f, 1.65f + _wave * 0.04f);
            _monsters.Add(monster);
        }

        private void ForceAllAgentsToDecide()
        {
            foreach (var agent in _agents)
            {
                if (agent != null && agent.IsAlive)
                {
                    agent.Agent.ForceDecision();
                }
            }
        }
    }
}
