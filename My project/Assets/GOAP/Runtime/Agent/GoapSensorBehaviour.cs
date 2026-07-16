using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapSensorUpdateMode
    {
        EveryDecision,
        Interval,
        Manual,
        Event
    }

    public abstract class GoapSensorBehaviour : MonoBehaviour
    {
        [SerializeField] private GoapSensorUpdateMode _updateMode = GoapSensorUpdateMode.EveryDecision;
        [SerializeField, Min(0.05f)] private float _interval = 0.5f;

        private float _nextSenseTime;
        private bool _refreshRequested = true;

        public GoapSensorUpdateMode UpdateMode => _updateMode;

        public bool TickSense(GoapAgent agent, GoapWorldState state, bool force = false)
        {
            if (!force && !ShouldSense())
            {
                return false;
            }

            Sense(agent, state);
            _refreshRequested = false;
            _nextSenseTime = Time.time + Mathf.Max(0.05f, _interval);
            return true;
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
            OnRefreshRequested();
        }

        public abstract void Sense(GoapAgent agent, GoapWorldState state);

        protected virtual void OnRefreshRequested()
        {
        }

        private bool ShouldSense()
        {
            return _refreshRequested ||
                   _updateMode == GoapSensorUpdateMode.EveryDecision ||
                   (_updateMode == GoapSensorUpdateMode.Interval && Time.time >= _nextSenseTime);
        }
    }
}
