using System.Collections;
using UnityEngine;

namespace Practice.GOAP.Tests
{
    public sealed class GoapImmediateTestActionBehaviour : GoapActionBehaviour
    {
        protected override IEnumerator Perform(GoapActionContext context)
        {
            yield return null;
            Succeed();
        }
    }

    public sealed class GoapTimedTestActionBehaviour : GoapActionBehaviour
    {
        public float Duration { get; set; } = 0.25f;

        protected override IEnumerator Perform(GoapActionContext context)
        {
            yield return new WaitForSeconds(Duration);
            Succeed();
        }
    }
}
