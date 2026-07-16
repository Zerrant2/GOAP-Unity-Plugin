using UnityEngine;

namespace Practice.GOAP.Demo
{
    public sealed class GoapDemoHud : MonoBehaviour
    {
        private GoapDemoBootstrap _bootstrap;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _boxStyle;

        public void Configure(GoapDemoBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
        }

        private void OnGUI()
        {
            if (_bootstrap == null)
            {
                return;
            }

            EnsureStyles();
            var width = Mathf.Min(430f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, Screen.height - 24f), _boxStyle);
            GUILayout.Label("GOAP Runtime Monitor", _titleStyle);
            GUILayout.Space(4f);

            foreach (var agent in _bootstrap.Agents)
            {
                if (agent == null)
                {
                    continue;
                }

                GUILayout.Label(agent.name, _titleStyle);
                GUILayout.Label($"Goal: {(agent.CurrentGoal != null ? agent.CurrentGoal.DisplayName : "None")}", _labelStyle);
                GUILayout.Label($"Plan: {agent.GetPlanSummary()}", _labelStyle);
                GUILayout.Label(agent.StatusMessage, _labelStyle);
                GUILayout.Space(8f);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("World events", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Worker hungry")) _bootstrap.MakeWorkerHungry();
            if (GUILayout.Button("Resident tired")) _bootstrap.MakeResidentTired();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn enemy")) _bootstrap.SetEnemyAvailable(true);
            if (GUILayout.Button("Remove enemy")) _bootstrap.SetEnemyAvailable(false);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove food")) _bootstrap.SetFoodAvailable(false);
            if (GUILayout.Button("Restore food")) _bootstrap.SetFoodAvailable(true);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove bed")) _bootstrap.SetBedAvailable(false);
            if (GUILayout.Button("Restore bed")) _bootstrap.SetBedAvailable(true);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset demo")) _bootstrap.ResetDemo();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.93f, 0.96f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.82f, 0.86f, 0.9f) }
            };
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 12, 12),
                normal = { background = Texture2D.grayTexture }
            };
        }
    }
}
