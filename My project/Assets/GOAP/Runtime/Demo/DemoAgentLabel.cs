using UnityEngine;

namespace Practice.GOAP.Demo
{
    public sealed class DemoAgentLabel : MonoBehaviour
    {
        [SerializeField] private GoapAgent _agent;
        [SerializeField] private TextMesh _text;
        [SerializeField] private string _roleName;

        public void Configure(GoapAgent agent, string roleName, Color color)
        {
            _agent = agent;
            _roleName = roleName;

            var labelObject = new GameObject("Status Label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            _text = labelObject.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.LowerCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 44;
            _text.characterSize = 0.055f;
            _text.color = color;
        }

        private void LateUpdate()
        {
            if (_text == null || _agent == null)
            {
                return;
            }

            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform != null)
            {
                _text.transform.rotation = cameraTransform.rotation;
            }

            var goal = _agent.CurrentGoal != null ? _agent.CurrentGoal.DisplayName : "Idle";
            var action = _agent.CurrentAction != null ? _agent.CurrentAction.DisplayName : "Thinking";
            _text.text = $"{_roleName}\nGoal: {goal}\nAction: {action}";
        }
    }
}
