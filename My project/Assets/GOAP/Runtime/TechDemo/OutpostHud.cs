using System;
using UnityEngine;

namespace Practice.GOAP.TechDemo
{
    [DisallowMultipleComponent]
    public sealed class OutpostHud : MonoBehaviour
    {
        [SerializeField] private OutpostGameController _controller;

        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _selectedButtonStyle;
        private Texture2D _panelTexture;
        private OutpostAgent _selectedAgent;
        private Vector2 _rosterScroll;

        public void Configure(OutpostGameController controller)
        {
            _controller = controller;
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                return;
            }

            if (_selectedAgent != null && !_selectedAgent.IsAlive)
            {
                Select(null);
            }

            EnsureStyles();
            DrawTopBar();
            DrawRoster();
            DrawCommands();
            DrawEventLog();
            DrawGameOver();
        }

        private void DrawTopBar()
        {
            var width = Mathf.Max(560f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, 70f), _panelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("GOAP OUTPOST", _titleStyle, GUILayout.Width(190f));
            GUILayout.Label($"WOOD  {_controller.Stockpile.Wood}/{_controller.WoodTarget}", _headerStyle, GUILayout.Width(145f));
            GUILayout.Label($"FOOD  {_controller.Stockpile.Food}/{_controller.FoodTarget}", _headerStyle, GUILayout.Width(145f));
            var campRatio = _controller.Camp.Health / _controller.Camp.MaxHealth;
            GUILayout.Label($"CAMP  {campRatio:P0}", _headerStyle, GUILayout.Width(125f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                _controller.HasLivingMonsters
                    ? $"WAVE {_controller.Wave}  |  {_controller.Monsters.Count} HOSTILES"
                    : $"NEXT WAVE  {_controller.TimeUntilNextWave:0}s",
                _headerStyle,
                GUILayout.Width(210f));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawRoster()
        {
            var height = Mathf.Min(Screen.height - 188f, 470f);
            GUILayout.BeginArea(new Rect(12f, 94f, 310f, height), _panelStyle);
            GUILayout.Label("AGENTS", _headerStyle);
            _rosterScroll = GUILayout.BeginScrollView(_rosterScroll);
            foreach (var actor in _controller.Agents)
            {
                if (actor == null || !actor.IsAlive)
                {
                    continue;
                }

                var agent = actor.Agent;
                var goal = agent.CurrentGoal != null ? agent.CurrentGoal.DisplayName : "Idle";
                var content = $"#{actor.Index + 1}  {OutpostGameController.FormatRole(actor.Role)}\n" +
                              $"HP {actor.Health:0}  Hunger {actor.Hunger:0}  Energy {actor.Energy:0}\n" +
                              $"Goal: {goal}";
                if (GUILayout.Button(
                        content,
                        actor == _selectedAgent ? _selectedButtonStyle : GUI.skin.button,
                        GUILayout.Height(64f)))
                {
                    Select(actor);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCommands()
        {
            var x = Mathf.Max(334f, Screen.width - 340f);
            GUILayout.BeginArea(new Rect(x, 94f, 328f, 390f), _panelStyle);
            GUILayout.Label("COMMAND", _headerStyle);
            GUILayout.Label("Recruit agent", _mutedStyle);
            DrawRoleButtons(role => _controller.TryRecruit(role), $"Cost: {_controller.RecruitFoodCost} food");

            GUILayout.Space(12f);
            GUILayout.Label(
                _selectedAgent != null
                    ? $"Selected: Agent #{_selectedAgent.Index + 1}"
                    : "Select an agent in the roster",
                _mutedStyle);
            GUI.enabled = _selectedAgent != null && _selectedAgent.IsAlive;
            DrawRoleButtons(role => _controller.ChangeRole(_selectedAgent, role), "Change role");
            GUI.enabled = true;

            GUILayout.Space(12f);
            GUILayout.Label("Simulation", _mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pause")) Time.timeScale = 0f;
            if (GUILayout.Button("1x")) Time.timeScale = 1f;
            if (GUILayout.Button("2x")) Time.timeScale = 2f;
            if (GUILayout.Button("4x")) Time.timeScale = 4f;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Send next wave now", GUILayout.Height(28f)))
            {
                _controller.SpawnWave();
            }

            if (_selectedAgent != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label("GOAP DECISION", _headerStyle);
                var agent = _selectedAgent.Agent;
                GUILayout.Label($"Goal: {(agent.CurrentGoal != null ? agent.CurrentGoal.DisplayName : "None")}", _labelStyle);
                GUILayout.Label($"Plan: {agent.GetPlanSummary()}", _labelStyle);
                GUILayout.Label(agent.StatusMessage, _mutedStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawRoleButtons(Action<OutpostRole> onClick, string tooltip)
        {
            GUILayout.BeginHorizontal();
            foreach (OutpostRole role in Enum.GetValues(typeof(OutpostRole)))
            {
                if (GUILayout.Button(new GUIContent(ShortRole(role), tooltip), GUILayout.Height(30f)))
                {
                    onClick(role);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEventLog()
        {
            var width = Mathf.Min(620f, Screen.width - 24f);
            var height = Mathf.Min(150f, Screen.height - 106f);
            GUILayout.BeginArea(new Rect(12f, Screen.height - height - 12f, width, height), _panelStyle);
            GUILayout.Label("EVENT LOG", _headerStyle);
            foreach (var entry in _controller.EventLog)
            {
                GUILayout.Label(entry, _mutedStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawGameOver()
        {
            if (!_controller.IsGameOver)
            {
                return;
            }

            var rect = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 65f, 440f, 130f);
            GUILayout.BeginArea(rect, _panelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("OUTPOST LOST", _titleStyle);
            GUILayout.Label("The camp or every agent has been destroyed.", _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void Select(OutpostAgent actor)
        {
            _selectedAgent?.SetSelected(false);
            _selectedAgent = actor;
            _selectedAgent?.SetSelected(true);
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = new Texture2D(1, 1) { name = "Outpost HUD Background" };
            _panelTexture.SetPixel(0, 0, new Color(0.055f, 0.065f, 0.075f, 0.94f));
            _panelTexture.Apply();
            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                normal = { background = _panelTexture }
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.82f, 0.28f) }
            };
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.95f, 0.97f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.84f, 0.88f, 0.91f) }
            };
            _mutedStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.62f, 0.68f, 0.72f) }
            };
            _selectedButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.86f, 0.3f) }
            };
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
