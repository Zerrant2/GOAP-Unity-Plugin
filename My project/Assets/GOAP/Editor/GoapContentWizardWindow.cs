using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    public sealed class GoapContentWizardWindow : EditorWindow
    {
        private enum WizardPage
        {
            Agent,
            Action,
            Goal,
            BehaviourPreset,
            SmartObject
        }

        private enum ActionRecipeKind
        {
            Wait,
            MoveToNamedTarget,
            SmartObjectInteraction,
            GatherResource,
            ConsumeInventory,
            TriggerAnimation,
            InvokeEvent
        }

        private enum FactDestination
        {
            ActionPrecondition,
            ActionEffect,
            GoalActivation,
            GoalDesired
        }

        [SerializeField] private WizardPage _page;
        [SerializeField] private GoapDomain _domain;
        [SerializeField] private GoapAgentProfile _profile;
        [SerializeField] private GameObject _agentTarget;
        [SerializeField] private string _agentName = "GOAP Agent";
        [SerializeField] private string _profileName = "Agent Profile";
        [SerializeField] private bool _addInventory;
        [SerializeField] private bool _addStats;
        [SerializeField] private bool _visibleAgent = true;

        [SerializeField] private string _actionName = "New Action";
        [SerializeField] private string _actionExecutorId = "new-action";
        [SerializeField] private string _actionDescription = "";
        [SerializeField] private float _actionCost = 1f;
        [SerializeField] private List<GoapCondition> _actionPreconditions = new();
        [SerializeField] private List<GoapCondition> _actionEffects = new();
        [SerializeField] private ActionRecipeKind _actionRecipe;
        [SerializeField] private string _recipeCategory = "Resource";
        [SerializeField] private string _recipeTargetId = "Target";
        [SerializeField] private string _recipeItemId = "Resource";
        [SerializeField] private string _recipeEventId = "Action";
        [SerializeField] private int _recipeAmount = 1;
        [SerializeField] private float _recipeDuration = 0.5f;
        [SerializeField] private float _recipeMoveSpeed = 3.5f;
        [SerializeField] private float _recipeRange = 1.1f;
        [SerializeField] private bool _recipeUseNavMesh;
        [SerializeField] private bool _recipeReserve = true;
        [SerializeField] private bool _recipeConsumeTarget;

        [SerializeField] private string _goalName = "New Goal";
        [SerializeField] private string _goalDescription = "";
        [SerializeField] private int _customGoalPriority = 10;
        [SerializeField] private List<GoapCondition> _goalActivation = new();
        [SerializeField] private List<GoapCondition> _goalDesired = new();

        [SerializeField] private bool _showQuickFact;
        [SerializeField] private string _newFactName = "New Fact";
        [SerializeField] private GoapFactType _newFactType;
        [SerializeField] private bool _newFactBoolean;
        [SerializeField] private int _newFactInteger;
        [SerializeField] private float _newFactFloat;
        [SerializeField] private int _newFactEnumIndex;
        [SerializeField] private string _newFactEnumOptions = "None, Low, High";
        [SerializeField] private FactDestination _factDestination;

        [SerializeField] private GoapContentPresetKind _presetKind;
        [SerializeField] private string _resourceName = "Wood";
        [SerializeField] private string _resourceCategory = "Wood";
        [SerializeField] private string _itemId = "Wood";
        [SerializeField] private int _targetAmount = 1;
        [SerializeField] private int _goalPriority = 30;
        [SerializeField] private bool _presetCreateProfile = true;
        [SerializeField] private bool _presetCreateAgent = true;
        [SerializeField] private bool _presetCreateWorldObjects = true;

        [SerializeField] private GameObject _smartObjectTarget;
        [SerializeField] private string _smartObjectName = "Resource";
        [SerializeField] private string _smartObjectCategory = "Resource";
        [SerializeField] private int _smartObjectCapacity = 1;
        [SerializeField] private bool _smartObjectConsumed = true;
        [SerializeField] private PrimitiveType _smartObjectPrimitive = PrimitiveType.Cube;

        private Vector2 _scroll;
        private string _status;
        private MessageType _statusType = MessageType.Info;

        [MenuItem("Tools/GOAP/Content Wizard %#n")]
        public static void Open()
        {
            Open(null, Selection.activeGameObject);
        }

        public static void Open(GoapDomain domain, GameObject target = null)
        {
            OpenPage(WizardPage.Agent, domain, target);
        }

        public static void OpenAction(GoapDomain domain)
        {
            OpenPage(WizardPage.Action, domain, null);
        }

        public static void OpenGoal(GoapDomain domain)
        {
            OpenPage(WizardPage.Goal, domain, null);
        }

        private static void OpenPage(WizardPage page, GoapDomain domain, GameObject target)
        {
            var window = GetWindow<GoapContentWizardWindow>();
            window.titleContent = new GUIContent("GOAP Content");
            window.minSize = new Vector2(500f, 570f);
            window._page = page;
            if (domain != null)
            {
                window._domain = domain;
            }

            if (target != null)
            {
                window.UseAgentTarget(target);
            }

            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("GOAP Content");
            minSize = new Vector2(500f, 570f);
        }

        private void OnGUI()
        {
            DrawHeader();
            _page = (WizardPage)GUILayout.Toolbar(
                (int)_page,
                new[] { "Agent", "Action", "Goal", "Presets", "Smart Object" },
                GUILayout.Height(27f));
            EditorGUILayout.Space(8f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                switch (_page)
                {
                    case WizardPage.Agent:
                        DrawAgentPage();
                        break;
                    case WizardPage.Action:
                        DrawActionPage();
                        break;
                    case WizardPage.Goal:
                        DrawGoalPage();
                        break;
                    case WizardPage.BehaviourPreset:
                        DrawPresetPage();
                        break;
                    case WizardPage.SmartObject:
                        DrawSmartObjectPage();
                        break;
                }
            }

            EditorGUILayout.EndScrollView();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Content creation is disabled while entering or running Play Mode.", MessageType.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusType);
            }
        }

        private static void DrawHeader()
        {
            EditorGUILayout.LabelField("GOAP Content Wizard", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Create connected content without manually assembling every asset and component.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(7f);
        }

        private void DrawAgentPage()
        {
            DrawSectionTitle("Scene Agent");
            var nextTarget = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Existing Object", "Leave empty to create a new scene object."),
                _agentTarget,
                typeof(GameObject),
                true);
            if (nextTarget != _agentTarget)
            {
                UseAgentTarget(nextTarget);
            }

            if (_agentTarget == null)
            {
                _agentName = EditorGUILayout.TextField("Agent Name", _agentName);
                _visibleAgent = EditorGUILayout.Toggle(
                    new GUIContent("Visible Placeholder", "Create a capsule so the new agent is immediately visible."),
                    _visibleAgent);
            }

            EditorGUILayout.Space(8f);
            DrawSectionTitle("Profile");
            var nextProfile = (GoapAgentProfile)EditorGUILayout.ObjectField(
                "Existing Profile",
                _profile,
                typeof(GoapAgentProfile),
                false);
            if (nextProfile != _profile)
            {
                _profile = nextProfile;
                if (_profile != null)
                {
                    _domain = _profile.Domain;
                    _profileName = _profile.name;
                }
            }

            if (_profile == null)
            {
                _domain = (GoapDomain)EditorGUILayout.ObjectField("Domain", _domain, typeof(GoapDomain), false);
                _profileName = EditorGUILayout.TextField("New Profile Name", _profileName);
                EditorGUILayout.HelpBox(
                    "A profile using all Actions and Goals from this Domain will be created next to the Domain asset.",
                    MessageType.Info);
            }
            else if (_profile.Domain == null)
            {
                EditorGUILayout.HelpBox("The selected profile has no Domain.", MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField("Domain", _profile.Domain.name);
                EditorGUILayout.LabelField(
                    "Content",
                    $"{_profile.Actions.Count} Actions, {_profile.Goals.Count} Goals, {_profile.Sensors.Count} Sensors");
            }

            EditorGUILayout.Space(8f);
            DrawSectionTitle("Optional Components");
            _addInventory = EditorGUILayout.Toggle("Inventory", _addInventory);
            _addStats = EditorGUILayout.Toggle("Stats", _addStats);
            EditorGUILayout.Space(12f);

            var label = _agentTarget == null ? "Create Agent" : "Setup Selected Object";
            using (new EditorGUI.DisabledScope(!CanResolveProfile()))
            {
                if (GUILayout.Button(label, GUILayout.Height(34f)))
                {
                    Run(SetupAgent);
                }
            }
        }

        private void DrawActionPage()
        {
            DrawSectionTitle("Action Definition");
            _domain = (GoapDomain)EditorGUILayout.ObjectField("Domain", _domain, typeof(GoapDomain), false);
            var nextName = EditorGUILayout.TextField("Name", _actionName);
            if (nextName != _actionName)
            {
                var oldIdentifier = string.IsNullOrWhiteSpace(_actionName)
                    ? string.Empty
                    : GoapContentCreationService.CreateIdentifier(_actionName);
                _actionName = nextName;
                if (string.IsNullOrWhiteSpace(_actionExecutorId) || _actionExecutorId == oldIdentifier)
                {
                    _actionExecutorId = string.IsNullOrWhiteSpace(_actionName)
                        ? string.Empty
                        : GoapContentCreationService.CreateIdentifier(_actionName);
                }
            }

            _actionExecutorId = EditorGUILayout.TextField(
                new GUIContent("Executor ID", "Unique action ID. The universal sequence executor uses it for diagnostics."),
                _actionExecutorId);
            _actionCost = EditorGUILayout.FloatField("Cost", Mathf.Max(0.01f, _actionCost));
            EditorGUILayout.LabelField("Description");
            _actionDescription = EditorGUILayout.TextArea(_actionDescription, GUILayout.MinHeight(42f));
            DrawDuplicateActionWarning();
            EditorGUILayout.Space(10f);

            GoapContentBuilderGui.DrawConditionList(
                "Preconditions",
                _actionPreconditions,
                _domain,
                false,
                "Optional: the world state required before this Action can run.");
            EditorGUILayout.Space(6f);
            GoapContentBuilderGui.DrawConditionList(
                "Effects",
                _actionEffects,
                _domain,
                true,
                "Add at least one world-state change produced by this Action.");
            EditorGUILayout.Space(8f);
            DrawQuickFactCreator(true);
            EditorGUILayout.Space(10f);

            DrawSectionTitle("Execution Recipe");
            _actionRecipe = (ActionRecipeKind)EditorGUILayout.EnumPopup("Recipe", _actionRecipe);
            DrawActionRecipeSettings();
            var steps = CanBuildActionSteps() ? BuildActionSteps() : Array.Empty<GoapActionStep>();
            if (steps.Length > 0)
            {
                EditorGUILayout.LabelField(
                    "Generated Steps",
                    string.Join(" > ", steps.Select(step => FormatStepKind(step.Kind))),
                    EditorStyles.wordWrappedMiniLabel);
            }

            if (RecipeRequiresInventory())
            {
                EditorGUILayout.HelpBox(
                    "This recipe requires GoapInventory on every agent that can execute it. Content Wizard adds it automatically when using the Resource preset.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(!CanCreateAction()))
            {
                if (GUILayout.Button("Create Action", GUILayout.Height(34f)))
                {
                    Run(CreateCustomAction);
                }
            }
        }

        private void DrawGoalPage()
        {
            DrawSectionTitle("Goal Definition");
            _domain = (GoapDomain)EditorGUILayout.ObjectField("Domain", _domain, typeof(GoapDomain), false);
            _goalName = EditorGUILayout.TextField("Name", _goalName);
            _customGoalPriority = EditorGUILayout.IntField("Priority", _customGoalPriority);
            EditorGUILayout.LabelField("Description");
            _goalDescription = EditorGUILayout.TextArea(_goalDescription, GUILayout.MinHeight(42f));
            if (_domain != null && _domain.Goals.Any(goal =>
                    goal != null && string.Equals(goal.DisplayName, _goalName, StringComparison.Ordinal)))
            {
                EditorGUILayout.HelpBox($"Goal '{_goalName}' already exists in this Domain.", MessageType.Error);
            }

            EditorGUILayout.Space(10f);
            GoapContentBuilderGui.DrawConditionList(
                "Activation Conditions",
                _goalActivation,
                _domain,
                false,
                "Optional: when empty, the Goal is considered whenever its desired state is not satisfied.");
            EditorGUILayout.Space(6f);
            GoapContentBuilderGui.DrawConditionList(
                "Desired Conditions",
                _goalDesired,
                _domain,
                false,
                "Add at least one condition the planner must achieve.");
            EditorGUILayout.Space(8f);
            DrawQuickFactCreator(false);
            EditorGUILayout.Space(10f);
            DrawProducerAnalysis();
            EditorGUILayout.Space(12f);

            using (new EditorGUI.DisabledScope(!CanCreateGoal()))
            {
                if (GUILayout.Button("Create Goal", GUILayout.Height(34f)))
                {
                    Run(CreateCustomGoal);
                }
            }
        }

        private void DrawActionRecipeSettings()
        {
            switch (_actionRecipe)
            {
                case ActionRecipeKind.Wait:
                    _recipeDuration = EditorGUILayout.FloatField("Duration", Mathf.Max(0f, _recipeDuration));
                    break;
                case ActionRecipeKind.MoveToNamedTarget:
                    _recipeTargetId = EditorGUILayout.TextField("Named Target ID", _recipeTargetId);
                    DrawMovementSettings();
                    break;
                case ActionRecipeKind.SmartObjectInteraction:
                    _recipeCategory = EditorGUILayout.TextField("Smart Object Category", _recipeCategory);
                    _recipeReserve = EditorGUILayout.Toggle("Reserve Target", _recipeReserve);
                    _recipeConsumeTarget = EditorGUILayout.Toggle("Consume Target", _recipeConsumeTarget);
                    _recipeDuration = EditorGUILayout.FloatField("Interaction Duration", Mathf.Max(0f, _recipeDuration));
                    DrawMovementSettings();
                    break;
                case ActionRecipeKind.GatherResource:
                    _recipeCategory = EditorGUILayout.TextField("Smart Object Category", _recipeCategory);
                    _recipeItemId = EditorGUILayout.TextField("Inventory Item ID", _recipeItemId);
                    _recipeAmount = EditorGUILayout.IntField("Amount", Mathf.Max(1, _recipeAmount));
                    _recipeDuration = EditorGUILayout.FloatField("Gather Duration", Mathf.Max(0f, _recipeDuration));
                    DrawMovementSettings();
                    break;
                case ActionRecipeKind.ConsumeInventory:
                    _recipeItemId = EditorGUILayout.TextField("Inventory Item ID", _recipeItemId);
                    _recipeAmount = EditorGUILayout.IntField("Amount", Mathf.Max(1, _recipeAmount));
                    _recipeDuration = EditorGUILayout.FloatField("Duration", Mathf.Max(0f, _recipeDuration));
                    break;
                case ActionRecipeKind.TriggerAnimation:
                    _recipeEventId = EditorGUILayout.TextField("Animator Trigger", _recipeEventId);
                    _recipeDuration = EditorGUILayout.FloatField("Duration", Mathf.Max(0f, _recipeDuration));
                    break;
                case ActionRecipeKind.InvokeEvent:
                    _recipeEventId = EditorGUILayout.TextField("Event ID", _recipeEventId);
                    break;
            }
        }

        private void DrawMovementSettings()
        {
            _recipeUseNavMesh = EditorGUILayout.Toggle("Use NavMesh", _recipeUseNavMesh);
            _recipeMoveSpeed = EditorGUILayout.FloatField("Move Speed", Mathf.Max(0.1f, _recipeMoveSpeed));
            _recipeRange = EditorGUILayout.FloatField("Stop Range", Mathf.Max(0.1f, _recipeRange));
        }

        private void DrawQuickFactCreator(bool forAction)
        {
            _showQuickFact = EditorGUILayout.Foldout(_showQuickFact, "Create and Connect New Fact", true);
            if (!_showQuickFact)
            {
                return;
            }

            EditorGUI.indentLevel++;
            _newFactName = EditorGUILayout.TextField("Fact Name", _newFactName);
            _newFactType = (GoapFactType)EditorGUILayout.EnumPopup("Type", _newFactType);
            DrawNewFactDefaultValue();
            if (forAction)
            {
                var destination = _factDestination == FactDestination.ActionEffect ? 1 : 0;
                destination = EditorGUILayout.Popup("Connect To", destination, new[] { "Precondition", "Effect" });
                _factDestination = destination == 0
                    ? FactDestination.ActionPrecondition
                    : FactDestination.ActionEffect;
            }
            else
            {
                var destination = _factDestination == FactDestination.GoalDesired ? 1 : 0;
                destination = EditorGUILayout.Popup("Connect To", destination, new[] { "Activation", "Desired State" });
                _factDestination = destination == 0
                    ? FactDestination.GoalActivation
                    : FactDestination.GoalDesired;
            }

            using (new EditorGUI.DisabledScope(_domain == null || string.IsNullOrWhiteSpace(_newFactName)))
            {
                if (GUILayout.Button("Create and Connect Fact"))
                {
                    Run(CreateAndConnectFact);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawNewFactDefaultValue()
        {
            switch (_newFactType)
            {
                case GoapFactType.Integer:
                    _newFactInteger = EditorGUILayout.IntField("Default", _newFactInteger);
                    break;
                case GoapFactType.Float:
                    _newFactFloat = EditorGUILayout.FloatField("Default", _newFactFloat);
                    break;
                case GoapFactType.Enum:
                    _newFactEnumOptions = EditorGUILayout.TextField("Options", _newFactEnumOptions);
                    var options = ParseEnumOptions();
                    _newFactEnumIndex = options.Length == 0
                        ? 0
                        : EditorGUILayout.Popup("Default", Mathf.Clamp(_newFactEnumIndex, 0, options.Length - 1), options);
                    break;
                default:
                    _newFactBoolean = EditorGUILayout.Toggle("Default", _newFactBoolean);
                    break;
            }
        }

        private void DrawProducerAnalysis()
        {
            DrawSectionTitle("Reachability Preview");
            if (_domain == null || _goalDesired.Count == 0)
            {
                EditorGUILayout.HelpBox("Add Desired Conditions to see matching Action producers.", MessageType.Info);
                return;
            }

            foreach (var desired in _goalDesired.Where(condition => condition.IsValid))
            {
                var producers = _domain.Actions
                    .Where(action => action != null && action.Effects.Any(effect => effect.CanEstablish(desired)))
                    .Select(action => action.DisplayName)
                    .ToArray();
                if (producers.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Missing producer: {desired}",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        desired.ToString(),
                        $"Produced by {string.Join(", ", producers)}",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawPresetPage()
        {
            DrawSectionTitle("Behaviour Package");
            _domain = (GoapDomain)EditorGUILayout.ObjectField("Domain", _domain, typeof(GoapDomain), false);
            var nextPresetKind = (GoapContentPresetKind)EditorGUILayout.EnumPopup("Preset", _presetKind);
            if (nextPresetKind != _presetKind)
            {
                _presetKind = nextPresetKind;
                ApplyPresetDefaults();
            }
            EditorGUILayout.Space(7f);

            if (_presetKind == GoapContentPresetKind.BasicNeeds)
            {
                EditorGUILayout.HelpBox(
                    "Creates hunger and fatigue Facts, Take Food / Eat / Rest Actions, two Goals, and profile Sensors for Food and Bed.",
                    MessageType.Info);
                _agentName = EditorGUILayout.TextField("Agent Name", NormalizeDefault(_agentName, "Needs Agent"));
                _profileName = EditorGUILayout.TextField("Profile Name", NormalizeDefault(_profileName, "Basic Needs Profile"));
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Creates availability and inventory Facts, a reservable gathering Action, a collection Goal, and matching Sensors.",
                    MessageType.Info);
                _resourceName = EditorGUILayout.TextField("Resource Name", _resourceName);
                _resourceCategory = EditorGUILayout.TextField("Smart Object Category", _resourceCategory);
                _itemId = EditorGUILayout.TextField("Inventory Item ID", _itemId);
                _targetAmount = EditorGUILayout.IntField("Target Amount", Mathf.Max(1, _targetAmount));
                _goalPriority = EditorGUILayout.IntField("Goal Priority", _goalPriority);
                _agentName = EditorGUILayout.TextField("Agent Name", _agentName);
                _profileName = EditorGUILayout.TextField("Profile Name", _profileName);
            }

            EditorGUILayout.Space(8f);
            DrawSectionTitle("Create Together");
            _presetCreateProfile = EditorGUILayout.Toggle("Agent Profile", _presetCreateProfile);
            using (new EditorGUI.DisabledScope(!_presetCreateProfile))
            {
                _presetCreateAgent = EditorGUILayout.Toggle("Scene Agent", _presetCreateAgent);
            }

            if (!_presetCreateProfile)
            {
                _presetCreateAgent = false;
            }

            _presetCreateWorldObjects = EditorGUILayout.Toggle(
                _presetKind == GoapContentPresetKind.BasicNeeds ? "Food and Bed" : "Resource Object",
                _presetCreateWorldObjects);
            EditorGUILayout.Space(12f);

            using (new EditorGUI.DisabledScope(_domain == null))
            {
                if (GUILayout.Button("Add Preset", GUILayout.Height(34f)))
                {
                    Run(AddPreset);
                }
            }
        }

        private void DrawSmartObjectPage()
        {
            DrawSectionTitle("World Object");
            _smartObjectTarget = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Existing Object", "Leave empty to create a visible primitive."),
                _smartObjectTarget,
                typeof(GameObject),
                true);
            if (_smartObjectTarget == null)
            {
                _smartObjectName = EditorGUILayout.TextField("Object Name", _smartObjectName);
                _smartObjectPrimitive = (PrimitiveType)EditorGUILayout.EnumPopup("Placeholder", _smartObjectPrimitive);
            }

            _smartObjectCategory = EditorGUILayout.TextField("Category", _smartObjectCategory);
            _smartObjectCapacity = EditorGUILayout.IntField("Capacity", Mathf.Max(1, _smartObjectCapacity));
            _smartObjectConsumed = EditorGUILayout.Toggle(
                new GUIContent("Consume On Use", "The object becomes unavailable after a successful use."),
                _smartObjectConsumed);
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "The category must exactly match Find Smart Object and Smart Object Sensor settings.",
                MessageType.Info);
            EditorGUILayout.Space(12f);

            var label = _smartObjectTarget == null ? "Create Smart Object" : "Setup Selected Object";
            if (GUILayout.Button(label, GUILayout.Height(34f)))
            {
                Run(SetupSmartObject);
            }
        }

        private void DrawDuplicateActionWarning()
        {
            if (_domain == null || string.IsNullOrWhiteSpace(_actionExecutorId))
            {
                return;
            }

            var existing = _domain.FindAction(_actionExecutorId.Trim());
            if (existing != null)
            {
                EditorGUILayout.HelpBox(
                    $"Executor ID '{_actionExecutorId}' is already used by '{existing.DisplayName}'.",
                    MessageType.Error);
            }
        }

        private bool CanCreateAction()
        {
            return _domain != null &&
                   !string.IsNullOrWhiteSpace(_actionName) &&
                   !string.IsNullOrWhiteSpace(_actionExecutorId) &&
                   _domain.FindAction(_actionExecutorId.Trim()) == null &&
                   GoapContentBuilderGui.CanSubmit(_actionPreconditions, _domain, false) &&
                   GoapContentBuilderGui.CanSubmit(_actionEffects, _domain, true) &&
                   CanBuildActionSteps();
        }

        private bool CanBuildActionSteps()
        {
            return _actionRecipe switch
            {
                ActionRecipeKind.MoveToNamedTarget => !string.IsNullOrWhiteSpace(_recipeTargetId),
                ActionRecipeKind.SmartObjectInteraction => !string.IsNullOrWhiteSpace(_recipeCategory),
                ActionRecipeKind.GatherResource =>
                    !string.IsNullOrWhiteSpace(_recipeCategory) &&
                    !string.IsNullOrWhiteSpace(_recipeItemId) &&
                    _recipeAmount > 0,
                ActionRecipeKind.ConsumeInventory =>
                    !string.IsNullOrWhiteSpace(_recipeItemId) && _recipeAmount > 0,
                ActionRecipeKind.TriggerAnimation => !string.IsNullOrWhiteSpace(_recipeEventId),
                ActionRecipeKind.InvokeEvent => !string.IsNullOrWhiteSpace(_recipeEventId),
                _ => true
            };
        }

        private GoapActionStep[] BuildActionSteps()
        {
            var steps = new List<GoapActionStep>();
            switch (_actionRecipe)
            {
                case ActionRecipeKind.Wait:
                    steps.Add(GoapActionStep.Wait(_recipeDuration));
                    break;
                case ActionRecipeKind.MoveToNamedTarget:
                    steps.Add(GoapActionStep.MoveToNamedTarget(
                        _recipeTargetId.Trim(),
                        _recipeRange,
                        _recipeMoveSpeed,
                        _recipeUseNavMesh));
                    break;
                case ActionRecipeKind.SmartObjectInteraction:
                    steps.Add(GoapActionStep.Find(_recipeCategory.Trim()));
                    if (_recipeReserve)
                    {
                        steps.Add(GoapActionStep.Reserve());
                    }

                    steps.Add(GoapActionStep.Move(_recipeRange, _recipeMoveSpeed, _recipeUseNavMesh));
                    steps.Add(new GoapActionStep(GoapActionStepKind.Interact));
                    steps.Add(GoapActionStep.Wait(_recipeDuration));
                    if (_recipeConsumeTarget)
                    {
                        steps.Add(new GoapActionStep(GoapActionStepKind.ConsumeTarget));
                    }

                    if (_recipeReserve)
                    {
                        steps.Add(new GoapActionStep(GoapActionStepKind.ReleaseTarget));
                    }

                    break;
                case ActionRecipeKind.GatherResource:
                    steps.Add(GoapActionStep.Find(_recipeCategory.Trim()));
                    steps.Add(GoapActionStep.Reserve());
                    steps.Add(GoapActionStep.Move(_recipeRange, _recipeMoveSpeed, _recipeUseNavMesh));
                    steps.Add(new GoapActionStep(GoapActionStepKind.Interact));
                    steps.Add(GoapActionStep.Wait(_recipeDuration));
                    steps.Add(new GoapActionStep(GoapActionStepKind.ConsumeTarget));
                    steps.Add(GoapActionStep.Inventory(
                        GoapActionStepKind.InventoryAdd,
                        _recipeItemId.Trim(),
                        _recipeAmount));
                    steps.Add(new GoapActionStep(GoapActionStepKind.ReleaseTarget));
                    break;
                case ActionRecipeKind.ConsumeInventory:
                    steps.Add(GoapActionStep.Wait(_recipeDuration));
                    steps.Add(GoapActionStep.Inventory(
                        GoapActionStepKind.InventoryRemove,
                        _recipeItemId.Trim(),
                        _recipeAmount));
                    break;
                case ActionRecipeKind.TriggerAnimation:
                    steps.Add(GoapActionStep.Event(GoapActionStepKind.TriggerAnimation, _recipeEventId.Trim()));
                    if (_recipeDuration > 0f)
                    {
                        steps.Add(GoapActionStep.Wait(_recipeDuration));
                    }

                    break;
                case ActionRecipeKind.InvokeEvent:
                    steps.Add(GoapActionStep.Event(GoapActionStepKind.InvokeEvent, _recipeEventId.Trim()));
                    break;
            }

            return steps.ToArray();
        }

        private bool RecipeRequiresInventory()
        {
            return _actionRecipe == ActionRecipeKind.GatherResource ||
                   _actionRecipe == ActionRecipeKind.ConsumeInventory;
        }

        private void CreateCustomAction()
        {
            var action = GoapContentCreationService.CreateAction(
                _domain,
                _actionName,
                _actionCost,
                _actionExecutorId,
                _actionPreconditions,
                _actionEffects,
                BuildActionSteps(),
                _actionDescription);
            Selection.activeObject = action;
            EditorGUIUtility.PingObject(action);
            _status = $"Action '{action.DisplayName}' was created with {action.ExecutionSteps.Count} generated steps.";
        }

        private bool CanCreateGoal()
        {
            return _domain != null &&
                   !string.IsNullOrWhiteSpace(_goalName) &&
                   !_domain.Goals.Any(goal =>
                       goal != null && string.Equals(goal.DisplayName, _goalName, StringComparison.Ordinal)) &&
                   GoapContentBuilderGui.CanSubmit(_goalActivation, _domain, false) &&
                   GoapContentBuilderGui.CanSubmit(_goalDesired, _domain, true);
        }

        private void CreateCustomGoal()
        {
            var goal = GoapContentCreationService.CreateGoal(
                _domain,
                _goalName,
                _customGoalPriority,
                _goalActivation,
                _goalDesired,
                _goalDescription);
            Selection.activeObject = goal;
            EditorGUIUtility.PingObject(goal);
            _status = $"Goal '{goal.DisplayName}' was created.";
        }

        private void CreateAndConnectFact()
        {
            var options = ParseEnumOptions();
            var defaultValue = _newFactType switch
            {
                GoapFactType.Integer => GoapValue.From(_newFactInteger),
                GoapFactType.Float => GoapValue.From(_newFactFloat),
                GoapFactType.Enum => GoapValue.FromEnum(_newFactEnumIndex),
                _ => GoapValue.From(_newFactBoolean)
            };
            var fact = GoapContentCreationService.CreateFact(
                _domain,
                _newFactName,
                _newFactType,
                defaultValue,
                options);
            var destination = GetFactDestinationList(out var isEffect);
            if (destination.All(condition => condition.Fact != fact))
            {
                destination.Add(GoapContentBuilderGui.CreateDefaultCondition(fact, isEffect));
            }

            _newFactName = "New Fact";
            _status = $"Fact '{fact.DisplayName}' was created and connected.";
        }

        private List<GoapCondition> GetFactDestinationList(out bool isEffect)
        {
            isEffect = _factDestination == FactDestination.ActionEffect;
            return _factDestination switch
            {
                FactDestination.ActionEffect => _actionEffects,
                FactDestination.GoalActivation => _goalActivation,
                FactDestination.GoalDesired => _goalDesired,
                _ => _actionPreconditions
            };
        }

        private string[] ParseEnumOptions()
        {
            return (_newFactEnumOptions ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(option => option.Trim())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string FormatStepKind(GoapActionStepKind kind)
        {
            return kind switch
            {
                GoapActionStepKind.FindSmartObject => "Find",
                GoapActionStepKind.ReserveTarget => "Reserve",
                GoapActionStepKind.MoveToTarget => "Move",
                GoapActionStepKind.ConsumeTarget => "Consume Target",
                GoapActionStepKind.InventoryAdd => "Inventory Add",
                GoapActionStepKind.InventoryRemove => "Inventory Remove",
                GoapActionStepKind.TriggerAnimation => "Animation",
                GoapActionStepKind.InvokeEvent => "Event",
                _ => kind.ToString()
            };
        }

        private void SetupAgent()
        {
            var profile = ResolveProfile();
            GameObject gameObject;
            if (_agentTarget != null)
            {
                GoapContentCreationService.SetupAgent(_agentTarget, profile, _addInventory, _addStats);
                gameObject = _agentTarget;
            }
            else
            {
                gameObject = GoapContentCreationService.CreateAgent(
                    _agentName,
                    profile,
                    _addInventory,
                    _addStats,
                    _visibleAgent,
                    GetSceneCreationPosition());
            }

            Selection.activeGameObject = gameObject;
            _status = $"'{gameObject.name}' is ready with profile '{profile.name}'.";
        }

        private void AddPreset()
        {
            var result = _presetKind == GoapContentPresetKind.BasicNeeds
                ? GoapContentCreationService.AddBasicNeedsPreset(_domain)
                : GoapContentCreationService.AddResourceGatheringPreset(
                    _domain,
                    _resourceName,
                    _resourceCategory,
                    _itemId,
                    _targetAmount,
                    _goalPriority);

            GoapAgentProfile profile = null;
            GameObject agent = null;
            var origin = GetSceneCreationPosition();
            if (_presetCreateProfile)
            {
                profile = GoapContentCreationService.CreateProfile(_domain, _profileName, result);
                _profile = profile;
            }

            if (_presetCreateAgent)
            {
                agent = GoapContentCreationService.CreateAgent(
                    _agentName,
                    profile,
                    result.RequiresInventory,
                    false,
                    true,
                    origin);
                _agentTarget = agent;
            }

            if (_presetCreateWorldObjects)
            {
                CreatePresetWorldObjects(origin);
            }

            if (agent != null)
            {
                Selection.activeGameObject = agent;
            }
            else if (profile != null)
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }
            else
            {
                Selection.activeObject = _domain;
                EditorGUIUtility.PingObject(_domain);
            }

            var createdText = result.CreatedDefinitionCount == 0
                ? "Existing definitions were reused."
                : $"Created {result.CreatedDefinitionCount} connected definitions.";
            _status = $"Preset added to '{_domain.name}'. {createdText}";
        }

        private void CreatePresetWorldObjects(Vector3 origin)
        {
            if (_presetKind == GoapContentPresetKind.BasicNeeds)
            {
                GoapContentCreationService.CreateSmartObject(
                    "Food",
                    "Food",
                    true,
                    1,
                    PrimitiveType.Cube,
                    origin + new Vector3(-2.5f, 0f, 3f),
                    new Vector3(0.8f, 0.8f, 0.8f));
                GoapContentCreationService.CreateSmartObject(
                    "Bed",
                    "Bed",
                    false,
                    1,
                    PrimitiveType.Cube,
                    origin + new Vector3(2.5f, 0f, 3f),
                    new Vector3(2.2f, 0.6f, 1.1f));
                return;
            }

            var resourceCount = Mathf.Max(1, _targetAmount);
            for (var index = 0; index < resourceCount; index++)
            {
                var column = index % 5;
                var row = index / 5;
                var objectName = resourceCount == 1 ? _resourceName : $"{_resourceName} {index + 1}";
                GoapContentCreationService.CreateSmartObject(
                    objectName,
                    _resourceCategory,
                    true,
                    1,
                    PrimitiveType.Cylinder,
                    origin + new Vector3(3f + column * 1.5f, 0f, 1.5f + row * 1.5f),
                    new Vector3(0.8f, 1.2f, 0.8f));
            }
        }

        private void SetupSmartObject()
        {
            GameObject gameObject;
            if (_smartObjectTarget != null)
            {
                GoapContentCreationService.SetupSmartObject(
                    _smartObjectTarget,
                    _smartObjectCategory,
                    _smartObjectConsumed,
                    _smartObjectCapacity);
                gameObject = _smartObjectTarget;
            }
            else
            {
                gameObject = GoapContentCreationService.CreateSmartObject(
                    _smartObjectName,
                    _smartObjectCategory,
                    _smartObjectConsumed,
                    _smartObjectCapacity,
                    _smartObjectPrimitive,
                    GetSceneCreationPosition());
            }

            Selection.activeGameObject = gameObject;
            _status = $"'{gameObject.name}' is ready as category '{_smartObjectCategory}'.";
        }

        private GoapAgentProfile ResolveProfile()
        {
            if (_profile != null)
            {
                if (_profile.Domain == null)
                {
                    throw new InvalidOperationException("The selected profile has no Domain.");
                }

                return _profile;
            }

            _profile = GoapContentCreationService.CreateProfile(
                _domain,
                _profileName,
                null,
                true);
            return _profile;
        }

        private bool CanResolveProfile()
        {
            return (_profile != null && _profile.Domain != null) || (_profile == null && _domain != null);
        }

        private void UseAgentTarget(GameObject target)
        {
            _agentTarget = target;
            if (_agentTarget == null)
            {
                return;
            }

            _agentName = _agentTarget.name;
            var authoring = _agentTarget.GetComponent<GoapAgentAuthoring>();
            if (authoring?.Profile != null)
            {
                _profile = authoring.Profile;
                _domain = _profile.Domain;
                _profileName = _profile.name;
            }

            _addInventory = _agentTarget.GetComponent<GoapInventory>() != null;
            _addStats = _agentTarget.GetComponent<GoapStatSource>() != null;
        }

        private void Run(Action action)
        {
            try
            {
                action();
                _statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                _statusType = MessageType.Error;
                Debug.LogException(exception);
            }

            Repaint();
        }

        private static string NormalizeDefault(string current, string fallback)
        {
            return string.IsNullOrWhiteSpace(current) || current == "GOAP Agent" || current == "Agent Profile"
                ? fallback
                : current;
        }

        private void ApplyPresetDefaults()
        {
            if (_presetKind == GoapContentPresetKind.BasicNeeds)
            {
                _agentName = "Needs Agent";
                _profileName = "Basic Needs Profile";
                return;
            }

            _agentName = $"{_resourceName} Gatherer";
            _profileName = $"{_resourceName} Gatherer Profile";
        }

        private static Vector3 GetSceneCreationPosition()
        {
            var sceneView = SceneView.lastActiveSceneView;
            return sceneView != null ? sceneView.pivot : Vector3.zero;
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
