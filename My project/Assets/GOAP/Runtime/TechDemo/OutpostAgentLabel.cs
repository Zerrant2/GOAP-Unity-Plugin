using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostAgentLabel : MonoBehaviour
    {
        [SerializeField] private OutpostAgent _actor;
        [SerializeField] private TextMesh _text;

        public void Configure(OutpostAgent actor)
        {
            _actor = actor;
            if (_text != null)
            {
                return;
            }

            var labelObject = new GameObject("Agent Status");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            _text = labelObject.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.LowerCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 42;
            _text.characterSize = 0.045f;
            _text.color = Color.white;
        }

        private void LateUpdate()
        {
            if (_actor == null || _text == null)
            {
                return;
            }

            if (Camera.main != null)
            {
                _text.transform.rotation = Camera.main.transform.rotation;
            }

            var agent = _actor.Agent;
            var action = agent.CurrentAction != null ? agent.CurrentAction.DisplayName : "Thinking";
            _text.text = $"{ShortRole(_actor.Role)} #{_actor.Index + 1}\n{action}";
        }

        private static string ShortRole(OutpostRole role)
        {
            return role switch
            {
                OutpostRole.Lumberjack => "WOOD",
                OutpostRole.Forager => "FOOD",
                OutpostRole.Guard => "GUARD",
                OutpostRole.Builder => "BUILD",
                _ => role.ToString().ToUpperInvariant()
            };
        }
    }
}
