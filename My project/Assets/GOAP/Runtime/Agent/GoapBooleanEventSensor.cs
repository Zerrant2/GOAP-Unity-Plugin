using UnityEngine;

namespace Practice.GOAP
{
    public sealed class GoapBooleanEventSensor : GoapSensorBehaviour
    {
        [SerializeField] private GoapFact _fact = null;
        [SerializeField] private bool _value;

        public override void Sense(GoapAgent agent, GoapWorldState state)
        {
            agent.SetFact(_fact, _value);
        }

        public void SetValue(bool value)
        {
            _value = value;
            RequestRefresh();
        }

        public void SetTrue()
        {
            SetValue(true);
        }

        public void SetFalse()
        {
            SetValue(false);
        }
    }
}
