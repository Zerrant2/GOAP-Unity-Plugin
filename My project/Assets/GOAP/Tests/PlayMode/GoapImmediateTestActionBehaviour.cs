using System.Collections;

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
}
