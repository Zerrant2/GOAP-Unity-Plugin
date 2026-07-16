using UnityEngine;

namespace Practice.GOAP.Demo
{
    public sealed class DemoAgentState : GoapSensorBehaviour
    {
        [SerializeField] private GoapFact _hungryFact;
        [SerializeField] private GoapFact _tiredFact;
        [SerializeField] private GoapFact _hasFoodFact;
        [SerializeField] private GoapFact _hasWeaponFact;
        [SerializeField] private bool _isHungry;
        [SerializeField] private bool _isTired;
        [SerializeField] private bool _hasFood;
        [SerializeField] private bool _hasWeapon;

        public bool IsHungry => _isHungry;
        public bool IsTired => _isTired;
        public bool HasFood => _hasFood;
        public bool HasWeapon => _hasWeapon;

        public void Configure(
            GoapFact hungryFact,
            GoapFact tiredFact,
            GoapFact hasFoodFact,
            GoapFact hasWeaponFact,
            bool hungry,
            bool tired)
        {
            _hungryFact = hungryFact;
            _tiredFact = tiredFact;
            _hasFoodFact = hasFoodFact;
            _hasWeaponFact = hasWeaponFact;
            ResetState(hungry, tired);
        }

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            agent.SetFact(_hungryFact, _isHungry);
            agent.SetFact(_tiredFact, _isTired);
            agent.SetFact(_hasFoodFact, _hasFood);
            agent.SetFact(_hasWeaponFact, _hasWeapon);
        }

        public void SetHungry(bool value)
        {
            _isHungry = value;
        }

        public void SetTired(bool value)
        {
            _isTired = value;
        }

        public void TakeFood()
        {
            _hasFood = true;
        }

        public bool Eat()
        {
            if (!_hasFood)
            {
                return false;
            }

            _hasFood = false;
            _isHungry = false;
            return true;
        }

        public void TakeWeapon()
        {
            _hasWeapon = true;
        }

        public void Rest()
        {
            _isTired = false;
        }

        public void ResetState(bool hungry, bool tired)
        {
            _isHungry = hungry;
            _isTired = tired;
            _hasFood = false;
            _hasWeapon = false;
        }
    }
}
