using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Practice.GOAP
{
    public sealed class GoapUnityEventActionBehaviour : GoapActionBehaviour
    {
        [SerializeField, Min(0f)] private float _duration = 0f;
        [SerializeField] private UnityEvent _onExecute = new();

        protected override IEnumerator Perform(GoapActionContext context)
        {
            if (_duration > 0f)
            {
                yield return new WaitForSeconds(_duration);
            }

            _onExecute.Invoke();
            Succeed();
        }
    }
}
