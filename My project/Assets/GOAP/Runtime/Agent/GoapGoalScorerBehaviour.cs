using UnityEngine;

namespace Practice.GOAP
{
    public abstract class GoapGoalScorerBehaviour : MonoBehaviour
    {
        [SerializeField] private GoapGoalDefinition _goal = null;
        [SerializeField] private string _label = "Scene score";

        public string Label => string.IsNullOrWhiteSpace(_label) ? GetType().Name : _label;

        public virtual bool Supports(GoapGoalDefinition goal)
        {
            return isActiveAndEnabled && (_goal == null || _goal == goal);
        }

        public abstract float EvaluateScore(
            GoapAgent agent,
            GoapGoalDefinition goal,
            GoapWorldState worldState);
    }
}
