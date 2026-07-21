using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Practice.GOAP.Editor
{
    public sealed class GoapEditorWindow : EditorWindow
    {
        private const string LastDomainKey = "Practice.GOAP.LastDomain";
        private const string RecentDomainsKey = "Practice.GOAP.RecentDomains";

        private ObjectField _domainField;
        private Toolbar _tabsToolbar;
        private ToolbarSearchField _searchField;
        private GoapGraphView _graphView;
        private ScrollView _library;
        private ScrollView _validationList;
        private IMGUIContainer _inspectorContainer;
        private HelpBox _validationBox;
        private UnityEditor.Editor _definitionEditor;
        private GoapDomain _domain;
        private GoapDefinition _selection;
        private double _nextRuntimeRefresh;

        [MenuItem("Tools/GOAP/Planner Graph %#g")]
        public static void Open()
        {
            var window = GetWindow<GoapEditorWindow>();
            window.titleContent = new GUIContent("GOAP Planner");
            window.minSize = new Vector2(980f, 580f);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = new Color(0.105f, 0.115f, 0.125f);
            BuildToolbar();
            BuildContent();
            RestoreLastDomain();
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += RefreshAll;
            GoapContentCreationService.DomainChanged += OnDomainContentChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= RefreshAll;
            GoapContentCreationService.DomainChanged -= OnDomainContentChanged;
            DestroyDefinitionEditor();
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            _domainField = new ObjectField("Domain")
            {
                objectType = typeof(GoapDomain),
                allowSceneObjects = false,
                style = { minWidth = 320f, flexGrow = 1f }
            };
            _domainField.RegisterValueChangedCallback(evt => SetDomain(evt.newValue as GoapDomain));
            toolbar.Add(_domainField);
            toolbar.Add(new ToolbarButton(CreateDomain) { text = "New Domain" });
            toolbar.Add(new ToolbarButton(() => GoapContentWizardWindow.Open(_domain))
            {
                text = "Content Wizard",
                tooltip = "Create agents, behaviour presets, profiles, and Smart Objects."
            });
            toolbar.Add(new ToolbarButton(Save) { text = "Save" });
            toolbar.Add(new ToolbarButton(ValidateDomain) { text = "Validate" });
            toolbar.Add(new ToolbarButton(GoapRuntimeDebuggerWindow.Open) { text = "Debugger" });
            toolbar.Add(new ToolbarButton(GoapDemoProjectBuilder.BuildOrRefreshDemo) { text = "Build Demo" });
            rootVisualElement.Add(toolbar);

            var graphToolbar = new Toolbar();
            graphToolbar.Add(new ToolbarButton(() => _graphView.SortGraph())
            {
                text = "Sort Graph",
                tooltip = "Arrange the full graph by causal flow and reduce edge crossings."
            });
            var focusToggle = new ToolbarToggle
            {
                text = "Focus",
                tooltip = "Dim unrelated nodes and connections when a node is selected."
            };
            focusToggle.SetValueWithoutNotify(true);
            focusToggle.RegisterValueChangedCallback(evt => _graphView?.SetFocusMode(evt.newValue));
            graphToolbar.Add(focusToggle);

            var detailsToggle = new ToolbarToggle
            {
                text = "Details",
                tooltip = "Show conditions and effects inside graph nodes."
            };
            detailsToggle.SetValueWithoutNotify(false);
            detailsToggle.RegisterValueChangedCallback(evt => _graphView?.SetDetailsVisible(evt.newValue));
            graphToolbar.Add(detailsToggle);

            var connectionsMenu = new ToolbarMenu
            {
                text = "Connections",
                tooltip = "Choose which connection types are visible."
            };
            connectionsMenu.menu.AppendAction(
                "Preconditions",
                _ => _graphView?.SetPreconditionsVisible(!_graphView.PreconditionsVisible),
                _ => _graphView != null && _graphView.PreconditionsVisible
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);
            connectionsMenu.menu.AppendAction(
                "Effects",
                _ => _graphView?.SetEffectsVisible(!_graphView.EffectsVisible),
                _ => _graphView != null && _graphView.EffectsVisible
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);
            connectionsMenu.menu.AppendAction(
                "Goal Links",
                _ => _graphView?.SetGoalLinksVisible(!_graphView.GoalLinksVisible),
                _ => _graphView != null && _graphView.GoalLinksVisible
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);
            graphToolbar.Add(connectionsMenu);
            graphToolbar.Add(new ToolbarButton(() => _graphView.FrameEverything()) { text = "Frame All" });
            rootVisualElement.Add(graphToolbar);
            _tabsToolbar = new Toolbar();
            rootVisualElement.Add(_tabsToolbar);
        }

        private void BuildContent()
        {
            var body = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    flexDirection = FlexDirection.Row
                }
            };

            var leftPanel = new VisualElement
            {
                style =
                {
                    width = 230f,
                    minWidth = 190f,
                    borderRightWidth = 1f,
                    borderRightColor = new Color(0.2f, 0.22f, 0.24f)
                }
            };
            leftPanel.Add(SectionTitle("Library"));
            _searchField = new ToolbarSearchField
            {
                style = { marginLeft = 7f, marginRight = 7f, marginBottom = 5f }
            };
            _searchField.RegisterValueChangedCallback(evt =>
            {
                RefreshLibrary();
                _graphView?.ApplySearch(evt.newValue);
            });
            leftPanel.Add(_searchField);
            _library = new ScrollView { style = { flexGrow = 1f } };
            leftPanel.Add(_library);

            _graphView = new GoapGraphView();
            _graphView.CreateRequested += CreateAtPosition;
            _graphView.BuilderRequested += OpenBuilder;
            _graphView.DuplicateRequested += DuplicateDefinitions;
            _graphView.ConnectedFactRequested += CreateConnectedFact;

            var rightPanel = new VisualElement
            {
                style =
                {
                    width = 310f,
                    minWidth = 260f,
                    borderLeftWidth = 1f,
                    borderLeftColor = new Color(0.2f, 0.22f, 0.24f)
                }
            };
            rightPanel.Add(SectionTitle("Inspector"));
            _inspectorContainer = new IMGUIContainer(DrawInspector) { style = { flexGrow = 1f, paddingLeft = 8f, paddingRight = 8f } };
            rightPanel.Add(_inspectorContainer);
            _validationBox = new HelpBox("Assign or create a domain to begin.", HelpBoxMessageType.Info)
            {
                style = { marginLeft = 8f, marginRight = 8f, marginBottom = 8f }
            };
            rightPanel.Add(_validationBox);
            _validationList = new ScrollView
            {
                style =
                {
                    maxHeight = 180f,
                    marginLeft = 8f,
                    marginRight = 8f,
                    marginBottom = 8f
                }
            };
            rightPanel.Add(_validationList);

            body.Add(leftPanel);
            body.Add(_graphView);
            body.Add(rightPanel);
            rootVisualElement.Add(body);
        }

        private static Label SectionTitle(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 13,
                    paddingLeft = 10f,
                    paddingTop = 9f,
                    paddingBottom = 8f
                }
            };
        }

        private void SetDomain(GoapDomain domain)
        {
            _domain = domain;
            _selection = null;
            DestroyDefinitionEditor();
            if (_domainField.value != domain)
            {
                _domainField.SetValueWithoutNotify(domain);
            }

            EditorPrefs.SetString(LastDomainKey, domain == null ? string.Empty : AssetDatabase.GetAssetPath(domain));
            RememberDomain(domain);
            RefreshDomainTabs();
            RefreshAll();
        }

        private void RememberDomain(GoapDomain domain)
        {
            if (domain == null)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(domain);
            var paths = EditorPrefs.GetString(RecentDomainsKey, string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(item => item != path && AssetDatabase.LoadAssetAtPath<GoapDomain>(item) != null)
                .Prepend(path)
                .Take(5);
            EditorPrefs.SetString(RecentDomainsKey, string.Join("|", paths));
        }

        private void RefreshDomainTabs()
        {
            if (_tabsToolbar == null)
            {
                return;
            }

            _tabsToolbar.Clear();
            var paths = EditorPrefs.GetString(RecentDomainsKey, string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                var tabDomain = AssetDatabase.LoadAssetAtPath<GoapDomain>(path);
                if (tabDomain == null)
                {
                    continue;
                }

                var toggle = new ToolbarToggle
                {
                    text = tabDomain.name,
                    value = tabDomain == _domain,
                    tooltip = path
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue && tabDomain != _domain)
                    {
                        SetDomain(tabDomain);
                    }
                });
                _tabsToolbar.Add(toggle);
            }

            _tabsToolbar.style.display = _tabsToolbar.childCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RestoreLastDomain()
        {
            var path = EditorPrefs.GetString(LastDomainKey, string.Empty);
            SetDomain(string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<GoapDomain>(path));
        }

        private void RefreshAll()
        {
            RefreshLibrary();
            _graphView?.Rebuild(_domain, SelectDefinition);
            _graphView?.ApplySearch(_searchField?.value);
            ValidateDomain();
            _inspectorContainer?.MarkDirtyRepaint();
        }

        private void RefreshLibrary()
        {
            if (_library == null)
            {
                return;
            }

            _library.Clear();
            if (_domain == null)
            {
                _library.Add(new Label("No domain selected") { style = { opacity = 0.55f, marginLeft = 10f } });
                return;
            }

            AddLibraryGroup("Facts", _domain.Facts.Cast<GoapDefinition>().ToArray(), CreateFact);
            AddLibraryGroup("Actions", _domain.Actions.Cast<GoapDefinition>().ToArray(), CreateAction);
            AddLibraryGroup("Goals", _domain.Goals.Cast<GoapDefinition>().ToArray(), CreateGoal);
        }

        private void AddLibraryGroup(string title, GoapDefinition[] definitions, Action createAction)
        {
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 7f } };
            header.Add(new Label(title)
            {
                style =
                {
                    flexGrow = 1f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginLeft = 9f
                }
            });
            header.Add(new Button(createAction) { text = "+", tooltip = $"Create {title.TrimEnd('s')}" });
            _library.Add(header);

            var query = _searchField?.value?.Trim();
            foreach (var definition in definitions.Where(definition =>
                         definition != null &&
                         (string.IsNullOrWhiteSpace(query) ||
                          definition.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 6f, marginRight = 4f } };
                var selectButton = new Button(() => SelectDefinition(definition))
                {
                    text = definition.DisplayName,
                    style = { flexGrow = 1f, unityTextAlign = TextAnchor.MiddleLeft }
                };
                row.Add(selectButton);
                row.Add(new Button(() => DuplicateDefinition(definition)) { text = "D", tooltip = $"Duplicate {definition.DisplayName}" });
                row.Add(new Button(() => DeleteDefinition(definition)) { text = "x", tooltip = $"Delete {definition.DisplayName}" });
                _library.Add(row);
            }
        }

        private void SelectDefinition(GoapDefinition definition)
        {
            _selection = definition;
            Selection.activeObject = definition;
            DestroyDefinitionEditor();
            if (_selection != null)
            {
                _definitionEditor = UnityEditor.Editor.CreateEditor(_selection);
            }

            _inspectorContainer.MarkDirtyRepaint();
            _graphView?.FocusDefinition(definition);
        }

        private void DrawInspector()
        {
            if (_definitionEditor == null)
            {
                EditorGUILayout.HelpBox("Select a fact, action, or goal.", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            _definitionEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_selection);
                rootVisualElement.schedule.Execute(RefreshAll);
            }
        }

        private void CreateDomain()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create GOAP Domain",
                "New GOAP Domain",
                "asset",
                "Choose where to save the GOAP domain.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var domain = CreateInstance<GoapDomain>();
            AssetDatabase.CreateAsset(domain, path);
            AssetDatabase.SaveAssets();
            SetDomain(domain);
        }

        private void CreateFact()
        {
            CreateSubAsset<GoapFact>("New Fact", definition => definition.Configure("New Fact", false));
        }

        private void CreateAction()
        {
            CreateSubAsset<GoapActionDefinition>("New Action", definition =>
                definition.Configure("New Action", 1f, string.Empty, null, null));
        }

        private void CreateGoal()
        {
            CreateSubAsset<GoapGoalDefinition>("New Goal", definition =>
                definition.Configure("New Goal", 1, null, null));
        }

        private T CreateSubAsset<T>(
            string undoName,
            Action<T> initialize,
            Vector2? graphPosition = null) where T : GoapDefinition
        {
            if (_domain == null)
            {
                return null;
            }

            var definition = CreateInstance<T>();
            initialize(definition);
            Undo.RegisterCreatedObjectUndo(definition, undoName);
            AssetDatabase.AddObjectToAsset(definition, _domain);
            Undo.RecordObject(_domain, undoName);

            switch (definition)
            {
                case GoapFact fact:
                    _domain.AddFact(fact);
                    SetCreatedPosition(fact, GoapNodeKind.Fact, graphPosition);
                    break;
                case GoapActionDefinition action:
                    _domain.AddAction(action);
                    SetCreatedPosition(action, GoapNodeKind.Action, graphPosition);
                    break;
                case GoapGoalDefinition goal:
                    _domain.AddGoal(goal);
                    SetCreatedPosition(goal, GoapNodeKind.Goal, graphPosition);
                    break;
            }

            EditorUtility.SetDirty(_domain);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            SelectDefinition(definition);
            RefreshAll();
            return definition;
        }

        private void CreateAtPosition(GoapNodeKind kind, Vector2 position)
        {
            switch (kind)
            {
                case GoapNodeKind.Fact:
                    CreateSubAsset<GoapFact>("New Fact", item => item.Configure("New Fact", false), position);
                    break;
                case GoapNodeKind.Action:
                    CreateSubAsset<GoapActionDefinition>(
                        "New Action",
                        item => item.Configure("New Action", 1f, string.Empty, null, null),
                        position);
                    break;
                case GoapNodeKind.Goal:
                    CreateSubAsset<GoapGoalDefinition>(
                        "New Goal",
                        item => item.Configure("New Goal", 1, null, null),
                        position);
                    break;
            }
        }

        private void OpenBuilder(GoapNodeKind kind)
        {
            if (kind == GoapNodeKind.Action)
            {
                GoapContentWizardWindow.OpenAction(_domain);
            }
            else if (kind == GoapNodeKind.Goal)
            {
                GoapContentWizardWindow.OpenGoal(_domain);
            }
        }

        private void CreateConnectedFact(
            GoapDefinition owner,
            GoapGraphConnectionRole role,
            Vector2 position)
        {
            var fact = CreateSubAsset<GoapFact>(
                "Create Connected Fact",
                item => item.Configure("New Fact", false),
                position);
            if (fact == null || owner == null)
            {
                return;
            }

            Undo.RecordObject(owner, "Connect New GOAP Fact");
            var condition = new GoapCondition(fact, true);
            switch (role)
            {
                case GoapGraphConnectionRole.ActionPrecondition when owner is GoapActionDefinition action:
                    action.AddPrecondition(condition);
                    break;
                case GoapGraphConnectionRole.ActionEffect when owner is GoapActionDefinition action:
                    action.AddEffect(condition);
                    break;
                case GoapGraphConnectionRole.GoalActivation when owner is GoapGoalDefinition goal:
                    goal.AddActivationCondition(condition);
                    break;
                case GoapGraphConnectionRole.GoalDesired when owner is GoapGoalDefinition goal:
                    goal.AddDesiredCondition(condition);
                    break;
            }

            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssets();
            SelectDefinition(owner);
            RefreshAll();
        }

        private void DuplicateDefinition(GoapDefinition source)
        {
            switch (source)
            {
                case GoapFact fact:
                    if (fact.ValueType == GoapFactType.Enum)
                    {
                        CreateSubAsset<GoapFact>("Duplicate Fact", item =>
                            item.ConfigureEnum(
                                $"{fact.DisplayName} Copy",
                                fact.EnumOptions,
                                fact.DefaultTypedValue.Integer,
                                fact.Description));
                    }
                    else if (fact.ValueType == GoapFactType.Integer)
                    {
                        CreateSubAsset<GoapFact>("Duplicate Fact", item =>
                            item.ConfigureInteger($"{fact.DisplayName} Copy", fact.DefaultTypedValue.Integer, fact.Description));
                    }
                    else if (fact.ValueType == GoapFactType.Float)
                    {
                        CreateSubAsset<GoapFact>("Duplicate Fact", item =>
                            item.ConfigureFloat($"{fact.DisplayName} Copy", fact.DefaultTypedValue.Float, fact.Description));
                    }
                    else
                    {
                        CreateSubAsset<GoapFact>("Duplicate Fact", item =>
                            item.Configure($"{fact.DisplayName} Copy", fact.DefaultValue, fact.Description));
                    }

                    break;
                case GoapActionDefinition action:
                    CreateSubAsset<GoapActionDefinition>("Duplicate Action", item =>
                    {
                        item.Configure(
                            $"{action.DisplayName} Copy",
                            action.Cost,
                            action.ExecutorId,
                            action.Preconditions,
                            action.Effects,
                            action.Description);
                        item.ConfigureBuiltInExecution(action.BuiltInExecution);
                        if (action.BuiltInExecution.Mode == GoapExecutionMode.Sequence)
                        {
                            item.ConfigureExecutionSteps(action.ExecutionSteps);
                        }
                    });
                    break;
                case GoapGoalDefinition goal:
                    CreateSubAsset<GoapGoalDefinition>("Duplicate Goal", item =>
                        item.Configure(
                            $"{goal.DisplayName} Copy",
                            goal.Priority,
                            goal.ActivationConditions,
                            goal.DesiredState,
                            goal.Description));
                    break;
            }
        }

        private void DuplicateDefinitions(System.Collections.Generic.IReadOnlyList<GoapDefinition> definitions)
        {
            foreach (var definition in definitions.Where(item => item != null))
            {
                DuplicateDefinition(definition);
            }
        }

        private void SetCreatedPosition(GoapDefinition definition, GoapNodeKind kind, Vector2? position)
        {
            if (position.HasValue)
            {
                _domain.SetNodePosition(definition.Id, kind, position.Value);
            }
        }

        private void DeleteDefinition(GoapDefinition definition)
        {
            if (_domain == null || definition == null ||
                !EditorUtility.DisplayDialog("Delete GOAP definition", $"Delete '{definition.DisplayName}'?", "Delete", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(_domain, "Delete GOAP Definition");
            _domain.Remove(definition);
            if (_selection == definition)
            {
                _selection = null;
                DestroyDefinitionEditor();
            }

            Undo.DestroyObjectImmediate(definition);
            EditorUtility.SetDirty(_domain);
            AssetDatabase.SaveAssets();
            RefreshAll();
        }

        private void Save()
        {
            if (_domain != null)
            {
                EditorUtility.SetDirty(_domain);
            }

            AssetDatabase.SaveAssets();
            ValidateDomain();
        }

        private void ValidateDomain()
        {
            if (_validationBox == null)
            {
                return;
            }

            if (_domain == null)
            {
                _validationBox.text = "Assign or create a domain to begin.";
                _validationBox.messageType = HelpBoxMessageType.Info;
                _validationList?.Clear();
                return;
            }

            var issues = GoapDomainValidator.Validate(_domain);
            _graphView?.SetValidationIssues(issues);
            _validationList?.Clear();
            var errors = issues.Count(issue => issue.Severity == GoapValidationSeverity.Error);
            var warnings = issues.Count(issue => issue.Severity == GoapValidationSeverity.Warning);
            if (issues.Count == 0)
            {
                _validationBox.text = "Domain is valid.";
                _validationBox.messageType = HelpBoxMessageType.Info;
                return;
            }

            _validationBox.text = $"{errors} errors, {warnings} warnings";
            _validationBox.messageType = errors > 0 ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
            foreach (var issue in issues)
            {
                var row = new VisualElement
                {
                    style = { flexDirection = FlexDirection.Row, marginBottom = 3f }
                };
                var button = new Button(() => SelectValidationIssue(issue))
                {
                    text = $"{issue.Severity}: {issue.Message}",
                    tooltip = issue.Message,
                    style =
                    {
                        whiteSpace = WhiteSpace.Normal,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        flexGrow = 1f
                    }
                };
                button.SetEnabled(issue.Source != null);
                row.Add(button);
                if (issue.FixKind != GoapValidationFixKind.None)
                {
                    row.Add(new Button(() => ApplyValidationFix(issue))
                    {
                        text = GetFixLabel(issue.FixKind),
                        tooltip = issue.FixKind.ToString()
                    });
                }

                _validationList?.Add(row);
            }
        }

        private void SelectValidationIssue(GoapValidationIssue issue)
        {
            if (issue.Source != null)
            {
                SelectDefinition(issue.Source);
            }
        }

        private void ApplyValidationFix(GoapValidationIssue issue)
        {
            switch (issue.FixKind)
            {
                case GoapValidationFixKind.CreateExecutor when issue.Source is GoapActionDefinition action:
                    Undo.RecordObject(action, "Create GOAP Executor");
                    action.ConfigureExecutionSteps(new[] { GoapActionStep.Wait(0.25f) });
                    EditorUtility.SetDirty(action);
                    SelectDefinition(action);
                    break;
                case GoapValidationFixKind.AddProducer when issue.RelatedCondition.IsValid:
                    var desired = issue.RelatedCondition;
                    CreateSubAsset<GoapActionDefinition>("Add GOAP Producer", producer =>
                    {
                        producer.Configure(
                            $"Produce {desired.Fact.DisplayName}",
                            1f,
                            string.Empty,
                            null,
                            new[] { CreateEstablishingEffect(desired) });
                        producer.ConfigureExecutionSteps(new[] { GoapActionStep.Wait(0.5f) });
                    });
                    break;
                case GoapValidationFixKind.OpenSensor:
                    OpenProfileForDomain();
                    break;
            }

            AssetDatabase.SaveAssets();
            RefreshAll();
        }

        private void OpenProfileForDomain()
        {
            var profile = AssetDatabase.FindAssets("t:GoapAgentProfile")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GoapAgentProfile>)
                .FirstOrDefault(item => item != null && item.Domain == _domain);
            if (profile == null)
            {
                var domainPath = AssetDatabase.GetAssetPath(_domain);
                var folder = System.IO.Path.GetDirectoryName(domainPath)?.Replace('\\', '/');
                var path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder}/{_domain.name} Agent Profile.asset");
                profile = CreateInstance<GoapAgentProfile>();
                profile.Configure(_domain);
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private static GoapCondition CreateEstablishingEffect(GoapCondition desired)
        {
            var fact = desired.Fact;
            if (fact.ValueType == GoapFactType.Boolean)
            {
                var value = desired.ExpectedValue.Boolean;
                return new GoapCondition(fact, desired.Comparison == GoapComparison.NotEqual ? !value : value);
            }

            if (fact.ValueType == GoapFactType.Float)
            {
                var value = desired.ExpectedValue.Float;
                value += desired.Comparison == GoapComparison.Greater ? 1f :
                    desired.Comparison == GoapComparison.Less ? -1f :
                    desired.Comparison == GoapComparison.NotEqual ? 1f : 0f;
                return new GoapCondition(fact, value);
            }

            var integer = desired.ExpectedValue.Integer;
            integer += desired.Comparison == GoapComparison.Greater ? 1 :
                desired.Comparison == GoapComparison.Less ? -1 :
                desired.Comparison == GoapComparison.NotEqual ? 1 : 0;
            if (fact.ValueType == GoapFactType.Enum)
            {
                integer = fact.NormalizeEnumIndex(integer);
            }

            return new GoapCondition(fact, integer);
        }

        private static string GetFixLabel(GoapValidationFixKind kind)
        {
            return kind switch
            {
                GoapValidationFixKind.CreateExecutor => "Create Executor",
                GoapValidationFixKind.AddProducer => "Add Producer",
                GoapValidationFixKind.OpenSensor => "Open Sensor",
                _ => "Fix"
            };
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextRuntimeRefresh)
            {
                return;
            }

            _nextRuntimeRefresh = EditorApplication.timeSinceStartup + 0.25d;
            _graphView?.UpdateRuntimeHighlights();
        }

        private void OnDomainContentChanged(GoapDomain domain)
        {
            if (domain == _domain)
            {
                RefreshAll();
            }
        }

        private void DestroyDefinitionEditor()
        {
            if (_definitionEditor != null)
            {
                DestroyImmediate(_definitionEditor);
                _definitionEditor = null;
            }
        }
    }
}
