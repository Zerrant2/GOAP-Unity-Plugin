using System.Collections;
using UnityEngine;

namespace Practice.GOAP
{
    public abstract class GoapActionBehaviour : MonoBehaviour
    {
        [SerializeField] private string _executorId;

        public string ExecutorId => _executorId;
        public GoapActionStatus Status { get; private set; } = GoapActionStatus.Idle;
        public string LastFailureReason { get; private set; } = string.Empty;

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
            return EvaluateStart(context).CanStart;
        }

        public virtual GoapExecutorDiagnostic EvaluateStart(GoapActionContext context)
        {
            return isActiveAndEnabled
                ? GoapExecutorDiagnostic.Ready()
                : GoapExecutorDiagnostic.Blocked(
                    GoapExecutorIssueCode.ExecutorDisabled,
                    $"Executor component '{GetType().Name}' is disabled");
        }

        public virtual bool CanContinue(GoapActionContext context)
        {
            return isActiveAndEnabled;
        }

        public IEnumerator Run(GoapActionContext context)
        {
            LastFailureReason = string.Empty;
            Status = GoapActionStatus.Running;
            yield return Perform(context);

            if (Status == GoapActionStatus.Running)
            {
                Fail("Executor finished without reporting success");
            }
        }

        public void Cancel(GoapActionContext context)
        {
            if (Status != GoapActionStatus.Running)
            {
                return;
            }

            Status = GoapActionStatus.Cancelled;
            LastFailureReason = "Action was cancelled";
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

        protected void Fail(string reason = null)
        {
            if (Status == GoapActionStatus.Running)
            {
                Status = GoapActionStatus.Failed;
                LastFailureReason = string.IsNullOrWhiteSpace(reason)
                    ? "Executor reported failure"
                    : reason;
            }
        }
    }
}
