using System.Collections;
using UnityEngine;

namespace Practice.GOAP
{
    public abstract class GoapActionBehaviour : MonoBehaviour
    {
        [SerializeField] private string _executorId;

        public string ExecutorId => _executorId;
        public GoapActionStatus Status { get; private set; } = GoapActionStatus.Idle;

        public void SetExecutorId(string executorId)
        {
            _executorId = executorId;
        }

        public virtual bool Supports(GoapActionDefinition action)
        {
            return action != null && !string.IsNullOrWhiteSpace(_executorId) && action.ExecutorId == _executorId;
        }

        public virtual bool CanStart(GoapActionContext context)
        {
            return isActiveAndEnabled;
        }

        public virtual bool CanContinue(GoapActionContext context)
        {
            return isActiveAndEnabled;
        }

        public IEnumerator Run(GoapActionContext context)
        {
            Status = GoapActionStatus.Running;
            yield return Perform(context);

            if (Status == GoapActionStatus.Running)
            {
                Status = GoapActionStatus.Failed;
            }
        }

        public void Cancel(GoapActionContext context)
        {
            if (Status != GoapActionStatus.Running)
            {
                return;
            }

            Status = GoapActionStatus.Cancelled;
            OnCancelled(context);
        }

        protected abstract IEnumerator Perform(GoapActionContext context);

        protected virtual void OnCancelled(GoapActionContext context)
        {
        }

        protected void Succeed()
        {
            if (Status == GoapActionStatus.Running)
            {
                Status = GoapActionStatus.Succeeded;
            }
        }

        protected void Fail()
        {
            if (Status == GoapActionStatus.Running)
            {
                Status = GoapActionStatus.Failed;
            }
        }
    }
}
