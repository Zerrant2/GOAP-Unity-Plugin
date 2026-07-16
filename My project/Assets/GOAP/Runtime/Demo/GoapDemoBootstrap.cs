using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP.Demo
{
    [DisallowMultipleComponent]
    public sealed class GoapDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GoapDomain _domain;
        [SerializeField] private List<GoapAgent> _agents = new();
        [SerializeField] private GoapAgent _worker;
        [SerializeField] private GoapAgent _resident;
        [SerializeField] private GoapAgent _guard;
        [SerializeField] private GoapAgent _survivor;
        [SerializeField] private List<GoapSmartObject> _food = new();
        [SerializeField] private GoapSmartObject _bed;
        [SerializeField] private GoapSmartObject _weapon;
        [SerializeField] private GoapSmartObject _enemy;
        [SerializeField] private GoapSmartObject _tree;

        public IReadOnlyList<GoapAgent> Agents => _agents;

        public void ConfigureAuthored(
            GoapDomain domain,
            IEnumerable<GoapAgent> agents,
            GoapAgent worker,
            GoapAgent resident,
            GoapAgent guard,
            GoapAgent survivor,
            IEnumerable<GoapSmartObject> food,
            GoapSmartObject bed,
            GoapSmartObject weapon,
            GoapSmartObject enemy,
            GoapSmartObject tree)
        {
            _domain = domain;
            _agents = agents == null ? new List<GoapAgent>() : new List<GoapAgent>(agents);
            _worker = worker;
            _resident = resident;
            _guard = guard;
            _survivor = survivor;
            _food = food == null ? new List<GoapSmartObject>() : new List<GoapSmartObject>(food);
            _bed = bed;
            _weapon = weapon;
            _enemy = enemy;
            _tree = tree;
        }

        private void Awake()
        {
            var hud = GetComponent<GoapDemoHud>();
            if (hud == null)
            {
                hud = gameObject.AddComponent<GoapDemoHud>();
            }

            hud.Configure(this);
        }

        public void MakeWorkerHungry()
        {
            SetFact(_worker, "Is Hungry", true);
        }

        public void MakeResidentTired()
        {
            SetFact(_resident, "Is Tired", true);
        }

        public void SetEnemyAvailable(bool available)
        {
            _enemy?.SetAvailable(available);
            ForceAllAgentsToDecide();
        }

        public void SetFoodAvailable(bool available)
        {
            foreach (var item in _food)
            {
                item?.SetAvailable(available);
            }

            ForceAllAgentsToDecide();
        }

        public void SetBedAvailable(bool available)
        {
            _bed?.SetAvailable(available);
            ForceAllAgentsToDecide();
        }

        public void ResetDemo()
        {
            SetFoodAvailable(true);
            _bed?.SetAvailable(true);
            _weapon?.SetAvailable(true);
            _enemy?.SetAvailable(true);
            _tree?.SetAvailable(true);
            SetAgentFacts(_worker, true, false);
            SetAgentFacts(_resident, false, true);
            SetAgentFacts(_guard, false, false);
            SetAgentFacts(_survivor, true, true);
            ForceAllAgentsToDecide();
        }

        private void SetAgentFacts(GoapAgent agent, bool hungry, bool tired)
        {
            if (agent == null || _domain == null)
            {
                return;
            }

            agent.SetFact(_domain.FindFact("Is Hungry"), hungry);
            agent.SetFact(_domain.FindFact("Is Tired"), tired);
            agent.SetFact(_domain.FindFact("Has Food"), false);
            agent.SetFact(_domain.FindFact("Has Weapon"), false);
        }

        private void SetFact(GoapAgent agent, string factName, bool value)
        {
            if (agent != null && _domain != null)
            {
                agent.SetFact(_domain.FindFact(factName), value);
                agent.ForceDecision();
            }
        }

        private void ForceAllAgentsToDecide()
        {
            foreach (var agent in _agents)
            {
                agent?.ForceDecision();
            }
        }
    }
}
