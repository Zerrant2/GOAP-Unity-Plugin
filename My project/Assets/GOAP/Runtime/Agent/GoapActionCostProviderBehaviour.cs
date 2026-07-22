using UnityEngine;

namespace Practice.GOAP
{
    public abstract class GoapActionCostProviderBehaviour : MonoBehaviour
    {
        [SerializeField] private GoapActionDefinition _action = null;

        public virtual bool Supports(GoapActionDefinition action)
        {
            return isActiveAndEnabled && (_action == null || _action == action);
        }

        public abstract float EvaluateAdditionalCost(
            GoapAgent agent,
            GoapActionDefinition action,
            Transform target);
    }
}
