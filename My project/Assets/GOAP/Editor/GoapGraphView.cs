using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Practice.GOAP.Editor
{
    public enum GoapGraphConnectionRole
    {
        ActionPrecondition,
        ActionEffect,
        GoalActivation,
        GoalDesired
    }

    public sealed class GoapGraphView : GraphView
    {
        private readonly Dictionary<GoapDefinition, Node> _nodes = new();
        private readonly Dictionary<GoapDefinition, GoapValidationSeverity> _validation = new();
        private bool _focusMode = true;
        private bool _showDetails;
        private bool _showPreconditions = true;
        private bool _showEffects = true;
        private bool _showGoalLinks = true;
        private GoapDomain _domain;
        private Action<GoapDefinition> _selectionChanged;
        private GoapDefinition _focusedDefinition;
        private string _searchQuery;
        private bool _rebuilding;
        private Port _dragSourcePort;
        private Vector2 _lastPointerPosition;
        private GoapAgent _runtimeAgent;
        private GoapDecisionSnapshot _runtimeSnapshot;

        public event Action<GoapNodeKind, Vector2> CreateRequested;
        public event Action<GoapNodeKind> BuilderRequested;
        public event Action<IReadOnlyList<GoapDefinition>> DuplicateRequested;
        public event Action<GoapDefinition, GoapGraphConnectionRole, Vector2> ConnectedFactRequested;

        public bool FocusMode => _focusMode;
        public bool DetailsVisible => _showDetails;
        public bool PreconditionsVisible => _showPreconditions;
        public bool EffectsVisible => _showEffects;
        public bool GoalLinksVisible => _showGoalLinks;
        public bool HasRuntimeContext => GetRuntimeDomain() == _domain && _runtimeAgent != null;

        public GoapGraphView()
        {
            style.flexGrow = 1f;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            var miniMap = new MiniMap { anchored = true };
            miniMap.SetPosition(new Rect(12f, 44f, 190f, 125f));
            Add(miniMap);
            graphViewChanged = OnGraphViewChanged;
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                _lastPointerPosition = this.ChangeCoordinatesTo(contentViewContainer, evt.localPosition);
            });
            RegisterCallback<MouseDownEvent>(evt =>
            {
                var target = evt.target as VisualElement;
                var graphElement = target as GraphElement ?? target?.GetFirstAncestorOfType<GraphElement>();
                if (evt.button == 0 && graphElement == null)
                {
                    _focusedDefinition = null;
                    RefreshGraphVisuals();
                }
            });
            nodeCreationRequest = _ => RequestConnectedFact();
            serializeGraphElements = elements => string.Join(",", elements
                .OfType<Node>()
                .Select(node => (node.userData as GoapDefinition)?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)));
            canPasteSerializedData = data => !string.IsNullOrWhiteSpace(data);
            unserializeAndPaste = (_, data) =>
            {
                var ids = data.Split(',');
                var definitions = _nodes.Keys.Where(item => ids.Contains(item.Id)).ToArray();
                if (definitions.Length > 0)
                {
                    DuplicateRequested?.Invoke(definitions);
                }
            };
        }

        public void Rebuild(GoapDomain domain, Action<GoapDefinition> selectionChanged)
        {
            _rebuilding = true;
            _domain = domain;
            _selectionChanged = selectionChanged;
            DeleteElements(graphElements.Where(element =>
                element is Node || element is Edge || element is Group || element is StickyNote).ToList());
            _nodes.Clear();

            if (_domain == null)
            {
                _rebuilding = false;
                return;
            }

            var factIndex = 0;
            foreach (var fact in _domain.Facts.Where(fact => fact != null))
            {
                var fallback = new Vector2(60f, 80f + factIndex * 135f);
                AddDefinitionNode(fact, CreateFactNode(fact, GetPosition(fact, GoapNodeKind.Fact, fallback)));
                factIndex++;
            }

            var actionIndex = 0;
            foreach (var action in _domain.Actions.Where(action => action != null))
            {
                var fallback = new Vector2(390f + (actionIndex % 2) * 320f, 80f + (actionIndex / 2) * 230f);
                AddDefinitionNode(action, CreateActionNode(action, GetPosition(action, GoapNodeKind.Action, fallback)));
                actionIndex++;
            }

            var goalIndex = 0;
            foreach (var goal in _domain.Goals.Where(goal => goal != null))
            {
                var fallback = new Vector2(1080f, 100f + goalIndex * 240f);
                AddDefinitionNode(goal, CreateGoalNode(goal, GetPosition(goal, GoapNodeKind.Goal, fallback)));
                goalIndex++;
            }

            AddFactEdges();
            ApplyDetailsState();
            AddAnnotations();
            if (_focusedDefinition != null && !_nodes.ContainsKey(_focusedDefinition))
            {
                _focusedDefinition = null;
            }

            _rebuilding = false;
            RefreshGraphVisuals();
        }

        public void FrameEverything()
        {
            FrameAll();
        }

        public void FocusDefinition(GoapDefinition definition)
        {
            if (definition == null || !_nodes.TryGetValue(definition, out var node))
            {
                return;
            }

            ClearSelection();
            AddToSelection(node);
            _focusedDefinition = definition;
            RefreshGraphVisuals();
            FrameSelection();
        }

        public void SortGraph()
        {
            if (_domain == null)
            {
                return;
            }

            var nodeRects = _nodes.ToDictionary(pair => pair.Key, pair => pair.Value.GetPosition());
            var positions = GoapGraphLayoutEngine.Calculate(_domain, nodeRects);
            Undo.RecordObject(_domain, "Sort GOAP Graph");
            foreach (var pair in positions)
            {
                StoreDefinitionPosition(pair.Key, pair.Value);
            }

            EditorUtility.SetDirty(_domain);
            Rebuild(_domain, _selectionChanged);
            schedule.Execute(() => FrameAll());
        }

        public void SortSelection()
        {
            var selectedNodes = selection.OfType<Node>().ToArray();
            if (selectedNodes.Length == 0)
            {
                SortGraph();
                return;
            }

            if (_domain == null)
            {
                return;
            }

            var definitions = selectedNodes
                .Select(node => node.userData as GoapDefinition)
                .Where(definition => definition != null)
                .ToArray();
            var nodeRects = _nodes.ToDictionary(pair => pair.Key, pair => pair.Value.GetPosition());
            var positions = GoapGraphLayoutEngine.Calculate(
                _domain,
                nodeRects,
                definitions,
                GetBounds(selectedNodes).position);
            Undo.RecordObject(_domain, "Sort GOAP Selection");
            foreach (var pair in positions)
            {
                if (!_nodes.TryGetValue(pair.Key, out var node))
                {
                    continue;
                }

                node.SetPosition(new Rect(pair.Value, node.GetPosition().size));
                StoreDefinitionPosition(pair.Key, pair.Value);
            }

            EditorUtility.SetDirty(_domain);
            schedule.Execute(() => FrameSelection());
        }

        public void AutoLayout()
        {
            SortGraph();
        }

        public void AutoLayoutSelection()
        {
            SortSelection();
        }

        public void SetFocusMode(bool enabled)
        {
            _focusMode = enabled;
            RefreshGraphVisuals();
        }

        public void SetDetailsVisible(bool visible)
        {
            if (_showDetails == visible)
            {
                return;
            }

            _showDetails = visible;
            Rebuild(_domain, _selectionChanged);
        }

        public void SetPreconditionsVisible(bool visible)
        {
            _showPreconditions = visible;
            RefreshGraphVisuals();
        }

        public void SetEffectsVisible(bool visible)
        {
            _showEffects = visible;
            RefreshGraphVisuals();
        }

        public void SetGoalLinksVisible(bool visible)
        {
            _showGoalLinks = visible;
            RefreshGraphVisuals();
        }

        public void ApplySearch(string query)
        {
            _searchQuery = query?.Trim();
            RefreshGraphVisuals();
        }

        public void SetRuntimeContext(GoapAgent agent, GoapDecisionSnapshot snapshot)
        {
            _runtimeAgent = agent;
            _runtimeSnapshot = snapshot;
            UpdateRuntimeHighlights();
        }

        public bool FrameRuntimePlan()
        {
            if (!HasRuntimeContext)
            {
                return false;
            }

            var definitions = new HashSet<GoapDefinition>();
            var goal = GetRuntimeGoal();
            if (goal != null)
            {
                definitions.Add(goal);
                foreach (var fact in goal.ActivationConditions.Concat(goal.DesiredState)
                             .Select(condition => condition.Fact)
                             .Where(fact => fact != null))
                {
                    definitions.Add(fact);
                }
            }

            foreach (var action in GetRuntimePlanActions())
            {
                definitions.Add(action);
                foreach (var fact in action.Preconditions.Concat(action.Effects)
                             .Select(condition => condition.Fact)
                             .Where(fact => fact != null))
                {
                    definitions.Add(fact);
                }
            }

            var planNodes = definitions
                .Where(definition => _nodes.ContainsKey(definition))
                .Select(definition => _nodes[definition])
                .ToArray();
            if (planNodes.Length == 0)
            {
                return false;
            }

            ClearSelection();
            foreach (var node in planNodes)
            {
                AddToSelection(node);
            }

            schedule.Execute(() => FrameSelection());
            return true;
        }

        public void SetValidationIssues(IReadOnlyList<GoapValidationIssue> issues)
        {
            _validation.Clear();
            foreach (var issue in issues.Where(issue => issue.Source != null))
            {
                if (!_validation.TryGetValue(issue.Source, out var current) || issue.Severity > current)
                {
                    _validation[issue.Source] = issue.Severity;
                }
            }

            foreach (var pair in _nodes)
            {
                pair.Value.Q<Label>("validation-badge")?.RemoveFromHierarchy();
                if (!_validation.TryGetValue(pair.Key, out var severity))
                {
                    SetNodeAccent(pair.Value, GetDefaultColor(pair.Key));
                    continue;
                }

                var color = severity == GoapValidationSeverity.Error
                    ? new Color(0.95f, 0.25f, 0.22f)
                    : severity == GoapValidationSeverity.Warning
                        ? new Color(0.96f, 0.68f, 0.18f)
                        : new Color(0.3f, 0.68f, 0.95f);
                SetNodeAccent(pair.Value, color);
                var badge = new Label(severity.ToString().ToUpperInvariant())
                {
                    name = "validation-badge",
                    tooltip = string.Join("\n", issues
                        .Where(issue => issue.Source == pair.Key)
                        .Select(issue => issue.Message)),
                    style =
                    {
                        color = color,
                        fontSize = 9,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginLeft = 5f
                    }
                };
                pair.Value.titleContainer.Add(badge);
            }
        }

        public void UpdateRuntimeHighlights()
        {
            RefreshGraphVisuals();
            foreach (var pair in _nodes)
            {
                SetNodeAccent(pair.Value, GetBaseColor(pair.Key));
                var runtimeBadge = pair.Value.Q<Label>("runtime-badge");
                if (runtimeBadge != null)
                {
                    runtimeBadge.style.display = DisplayStyle.None;
                }
                pair.Value.tooltip = pair.Key.Description;
            }

            if (!EditorApplication.isPlaying || !HasRuntimeContext)
            {
                return;
            }

            var diagnostics = GetRuntimeActionDiagnostics()
                .Where(item => item.Action != null)
                .GroupBy(item => item.Action)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var diagnostic in diagnostics.Values.Where(item => !item.Executable))
            {
                if (!_nodes.TryGetValue(diagnostic.Action, out var blockedNode))
                {
                    continue;
                }

                var label = diagnostic.HasExecutor ? "BLOCKED" : "NO EXECUTOR";
                var color = diagnostic.HasExecutor
                    ? new Color(0.88f, 0.32f, 0.28f)
                    : new Color(0.98f, 0.2f, 0.2f);
                var detail = GetActionRuntimeDetail(diagnostic);
                SetNodeAccent(blockedNode, color);
                SetRuntimeBadge(blockedNode, label, color, detail);
                blockedNode.tooltip = BuildRuntimeTooltip(diagnostic.Action.Description, detail);
            }

            foreach (var fact in _domain.Facts.Where(fact => fact != null))
            {
                var value = GetRuntimeValue(fact);
                if (value.Equals(fact.DefaultTypedValue) || !_nodes.TryGetValue(fact, out var factNode))
                {
                    continue;
                }

                var formattedValue = fact.FormatValue(value);
                var color = new Color(0.2f, 0.86f, 0.82f);
                SetNodeAccent(factNode, color);
                SetRuntimeBadge(factNode, formattedValue, color, $"Runtime value: {formattedValue}");
                factNode.tooltip = BuildRuntimeTooltip(fact.Description, $"Runtime value: {formattedValue}");
            }

            var planActions = GetRuntimePlanActions();
            var currentAction = GetRuntimeAction();
            var planGroups = planActions
                .Select((action, index) => new { Action = action, Position = index + 1 })
                .Where(item => item.Action != null)
                .GroupBy(item => item.Action);
            foreach (var group in planGroups)
            {
                var action = group.Key;
                if (!_nodes.TryGetValue(action, out var actionNode))
                {
                    continue;
                }

                var running = action == currentAction;
                var color = running
                    ? new Color(0.2f, 0.9f, 0.5f)
                    : new Color(0.98f, 0.74f, 0.18f);
                var positions = string.Join(",", group.Select(item => item.Position));
                var label = running ? $"RUNNING | {positions}" : $"PLAN {positions}";
                diagnostics.TryGetValue(action, out var diagnostic);
                var detail = diagnostic.Action != null ? GetActionRuntimeDetail(diagnostic) : "Planned action";
                SetNodeAccent(actionNode, color, 4f);
                SetRuntimeBadge(actionNode, label, color, detail);
                actionNode.tooltip = BuildRuntimeTooltip(action.Description, detail);
            }

            var currentGoal = GetRuntimeGoal();
            if (currentGoal != null && _nodes.TryGetValue(currentGoal, out var goalNode))
            {
                var color = new Color(0.25f, 0.68f, 1f);
                var diagnostic = GetRuntimeGoalDiagnostics().FirstOrDefault(item => item.Goal == currentGoal);
                var detail = diagnostic.Goal != null ? diagnostic.Reason : "Selected goal";
                SetNodeAccent(goalNode, color, 4f);
                SetRuntimeBadge(goalNode, "GOAL", color, detail);
                goalNode.tooltip = BuildRuntimeTooltip(currentGoal.Description, detail);
            }

            foreach (var edge in edges)
            {
                if (edge.userData is not GoapGraphEdgeBinding binding)
                {
                    continue;
                }

                if (binding.Owner == currentGoal)
                {
                    ApplyRuntimeEdgeStyle(edge, new Color(0.25f, 0.68f, 1f));
                    continue;
                }

                if (binding.Owner is GoapActionDefinition action && planActions.Contains(action))
                {
                    ApplyRuntimeEdgeStyle(
                        edge,
                        action == currentAction
                            ? new Color(0.2f, 0.9f, 0.5f)
                            : new Color(0.98f, 0.74f, 0.18f));
                }
            }
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            if (_domain == null)
            {
                return;
            }

            var position = this.ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Create/Fact", _ => CreateRequested?.Invoke(GoapNodeKind.Fact, position));
            evt.menu.AppendAction("Create/Action", _ => CreateRequested?.Invoke(GoapNodeKind.Action, position));
            evt.menu.AppendAction("Create/Action with Wizard", _ => BuilderRequested?.Invoke(GoapNodeKind.Action));
            evt.menu.AppendAction("Create/Goal", _ => CreateRequested?.Invoke(GoapNodeKind.Goal, position));
            evt.menu.AppendAction("Create/Goal with Wizard", _ => BuilderRequested?.Invoke(GoapNodeKind.Goal));
            evt.menu.AppendAction("Layout/Sort Graph", _ => SortGraph());
            evt.menu.AppendAction("Layout/Sort Selection", _ => SortSelection());
            evt.menu.AppendAction("Layout/Align Left", _ => AlignSelection(true));
            evt.menu.AppendAction("Layout/Align Top", _ => AlignSelection(false));
            evt.menu.AppendAction("Layout/Distribute Horizontally", _ => DistributeSelection(true));
            evt.menu.AppendAction("Layout/Distribute Vertically", _ => DistributeSelection(false));
            evt.menu.AppendAction("Layout/Frame All", _ => FrameEverything());
            var selectedDefinitions = selection.OfType<Node>()
                .Select(node => node.userData as GoapDefinition)
                .Where(item => item != null)
                .ToArray();
            if (selectedDefinitions.Length > 0)
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Duplicate Selection", _ => DuplicateRequested?.Invoke(selectedDefinitions));
            }

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Annotations/Group Selection", _ => CreateGroup(position));
            evt.menu.AppendAction("Annotations/Note", _ => CreateNote(position));
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.Where(port => port != startPort && port.node != startPort.node && IsCompatible(startPort, port)).ToList();
        }

        private void AddDefinitionNode(GoapDefinition definition, Node node)
        {
            node.capabilities &= ~Capabilities.Deletable;
            AddElement(node);
            _nodes.Add(definition, node);
        }

        private Port CreatePort(Orientation orientation, Direction direction)
        {
            return new GoapPort(
                orientation,
                direction,
                Port.Capacity.Multi,
                typeof(GoapValue),
                new GoapEdgeConnectorListener(this));
        }

        private Node CreateFactNode(GoapFact fact, Vector2 position)
        {
            var node = CreateNode(fact, position, new Vector2(245f, 105f), new Color(0.2f, 0.62f, 0.65f));
            var input = CreatePort(Orientation.Horizontal, Direction.Input);
            input.portName = "Effects";
            input.name = "setters";
            node.inputContainer.Add(input);
            var output = CreatePort(Orientation.Horizontal, Direction.Output);
            output.portName = "Conditions";
            output.name = "value";
            node.outputContainer.Add(output);
            TrackConnectionPort(input);
            TrackConnectionPort(output);
            node.extensionContainer.Add(CreateMetaLabel(
                $"{fact.ValueType} | Default: {fact.FormatValue(fact.DefaultTypedValue)}"));
            node.RefreshExpandedState();
            node.RefreshPorts();
            return node;
        }

        private Node CreateActionNode(GoapActionDefinition action, Vector2 position)
        {
            var node = CreateNode(action, position, new Vector2(270f, 180f), new Color(0.35f, 0.39f, 0.43f));
            var input = CreatePort(Orientation.Horizontal, Direction.Input);
            input.portName = "Preconditions";
            input.name = "preconditions";
            node.inputContainer.Add(input);
            var output = CreatePort(Orientation.Horizontal, Direction.Output);
            output.portName = "Effects";
            output.name = "effects";
            node.outputContainer.Add(output);
            TrackConnectionPort(input);
            TrackConnectionPort(output);

            var executor = action.UsesBuiltInExecutor
                ? action.BuiltInExecution.Mode.ToString()
                : string.IsNullOrWhiteSpace(action.ExecutorId) ? "Missing" : action.ExecutorId;
            node.extensionContainer.Add(CreateMetaLabel($"Cost {action.Cost:0.##} | Executor: {executor}"));
            node.extensionContainer.Add(CreateConditionBlock("Requires", action.Preconditions, new Color(0.88f, 0.72f, 0.3f)));
            node.extensionContainer.Add(CreateConditionBlock("Changes", action.Effects, new Color(0.38f, 0.82f, 0.58f)));
            node.RefreshExpandedState();
            node.RefreshPorts();
            return node;
        }

        private Node CreateGoalNode(GoapGoalDefinition goal, Vector2 position)
        {
            var node = CreateNode(goal, position, new Vector2(280f, 190f), new Color(0.32f, 0.62f, 0.88f));
            var activation = CreatePort(Orientation.Horizontal, Direction.Input);
            activation.portName = "Activation";
            activation.name = "activation";
            node.inputContainer.Add(activation);
            var desired = CreatePort(Orientation.Horizontal, Direction.Input);
            desired.portName = "Desired state";
            desired.name = "desired";
            node.inputContainer.Add(desired);
            TrackConnectionPort(activation);
            TrackConnectionPort(desired);
            node.extensionContainer.Add(CreateMetaLabel($"Priority {goal.Priority}"));
            node.extensionContainer.Add(CreateConditionBlock("Active when", goal.ActivationConditions, new Color(0.88f, 0.72f, 0.3f)));
            node.extensionContainer.Add(CreateConditionBlock("Wants", goal.DesiredState, new Color(0.4f, 0.74f, 1f)));
            node.RefreshExpandedState();
            node.RefreshPorts();
            return node;
        }

        private Node CreateNode(GoapDefinition definition, Vector2 position, Vector2 size, Color color)
        {
            var resolvedSize = _showDetails ? size : GetCompactNodeSize(definition, size.x);
            var node = new Node
            {
                title = definition.DisplayName,
                userData = definition,
                tooltip = definition.Description,
                expanded = true
            };
            node.SetPosition(new Rect(position, resolvedSize));
            SetNodeAccent(node, definition.HasCustomNodeColor ? definition.NodeColor : color);
            if (definition.Icon != null)
            {
                node.titleContainer.Insert(0, new Image
                {
                    image = definition.Icon,
                    scaleMode = ScaleMode.ScaleToFit,
                    style = { width = 18f, height = 18f, marginRight = 4f }
                });
            }
            node.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _focusedDefinition = definition;
                    RefreshGraphVisuals();
                    _selectionChanged?.Invoke(definition);
                }
            });
            return node;
        }

        private void AddFactEdges()
        {
            foreach (var action in _domain.Actions.Where(action => action != null))
            {
                if (!_nodes.TryGetValue(action, out var actionNode))
                {
                    continue;
                }

                var input = actionNode.inputContainer.Q<Port>("preconditions");
                var output = actionNode.outputContainer.Q<Port>("effects");
                foreach (var condition in action.Preconditions.Where(condition => condition.Fact != null))
                {
                    AddFactReaderEdge(condition.Fact, input);
                }

                foreach (var effect in action.Effects.Where(effect => effect.Fact != null))
                {
                    AddFactWriterEdge(output, effect.Fact);
                }
            }

            foreach (var goal in _domain.Goals.Where(goal => goal != null))
            {
                if (!_nodes.TryGetValue(goal, out var goalNode))
                {
                    continue;
                }

                foreach (var condition in goal.ActivationConditions.Where(condition => condition.Fact != null))
                {
                    AddFactReaderEdge(condition.Fact, goalNode.inputContainer.Q<Port>("activation"));
                }

                foreach (var condition in goal.DesiredState.Where(condition => condition.Fact != null))
                {
                    AddFactReaderEdge(condition.Fact, goalNode.inputContainer.Q<Port>("desired"));
                }
            }
        }

        private void AddFactReaderEdge(GoapFact fact, Port input)
        {
            if (_nodes.TryGetValue(fact, out var factNode))
            {
                var kind = input.name == "activation"
                    ? GoapGraphBindingKind.GoalActivation
                    : input.name == "desired"
                        ? GoapGraphBindingKind.GoalDesired
                        : GoapGraphBindingKind.ActionPrecondition;
                AddExistingEdge(
                    factNode.outputContainer.Q<Port>("value"),
                    input,
                    new GoapGraphEdgeBinding(fact, input.node.userData as GoapDefinition, kind));
            }
        }

        private void AddFactWriterEdge(Port output, GoapFact fact)
        {
            if (_nodes.TryGetValue(fact, out var factNode))
            {
                AddExistingEdge(
                    output,
                    factNode.inputContainer.Q<Port>("setters"),
                    new GoapGraphEdgeBinding(
                        fact,
                        output.node.userData as GoapDefinition,
                        GoapGraphBindingKind.ActionEffect));
            }
        }

        private void AddExistingEdge(Port output, Port input, GoapGraphEdgeBinding binding)
        {
            if (output == null || input == null)
            {
                return;
            }

            var edge = output.ConnectTo(input);
            edge.userData = binding;
            ApplyEdgeStyle(edge, binding, false);
            AddElement(edge);
        }

        private Vector2 GetPosition(GoapDefinition definition, GoapNodeKind kind, Vector2 fallback)
        {
            return _domain.TryGetNodePosition(definition.Id, kind, out var position) ? position : fallback;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_rebuilding || _domain == null)
            {
                return change;
            }

            if (change.edgesToCreate != null)
            {
                _dragSourcePort = null;
                var accepted = new List<Edge>();
                foreach (var edge in change.edgesToCreate)
                {
                    if (TryCreateBinding(edge, out var binding) && AddBinding(binding))
                    {
                        edge.userData = binding;
                        accepted.Add(edge);
                    }
                }

                change.edgesToCreate = accepted;
                schedule.Execute(() => Rebuild(_domain, _selectionChanged));
            }

            if (change.elementsToRemove != null)
            {
                var removedBinding = false;
                foreach (var edge in change.elementsToRemove.OfType<Edge>())
                {
                    if (edge.userData is GoapGraphEdgeBinding binding)
                    {
                        RemoveBinding(binding);
                        removedBinding = true;
                    }
                }

                foreach (var group in change.elementsToRemove.OfType<Group>())
                {
                    if (group.userData is GoapGraphGroupLayout layout)
                    {
                        Undo.RecordObject(_domain, "Delete GOAP Group");
                        _domain.RemoveGraphGroup(layout.Id);
                        EditorUtility.SetDirty(_domain);
                    }
                }

                foreach (var note in change.elementsToRemove.OfType<StickyNote>())
                {
                    if (note.userData is GoapGraphNoteLayout layout)
                    {
                        Undo.RecordObject(_domain, "Delete GOAP Note");
                        _domain.RemoveGraphNote(layout.Id);
                        EditorUtility.SetDirty(_domain);
                    }
                }

                if (removedBinding)
                {
                    schedule.Execute(() => Rebuild(_domain, _selectionChanged));
                }
            }

            var movedNodes = change.movedElements?.OfType<Node>().ToArray();
            if (movedNodes != null && movedNodes.Length > 0)
            {
                Undo.RecordObject(_domain, "Move GOAP Node");
                foreach (var element in movedNodes)
                {
                    StoreNodePosition(element);
                }

                EditorUtility.SetDirty(_domain);
            }


            foreach (var group in change.movedElements?.OfType<Group>() ?? Enumerable.Empty<Group>())
            {
                SaveGroup(group);
            }

            foreach (var note in change.movedElements?.OfType<StickyNote>() ?? Enumerable.Empty<StickyNote>())
            {
                SaveNote(note);
            }

            return change;
        }

        private void TrackConnectionPort(Port port)
        {
            port.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _dragSourcePort = port;
                }
            }, TrickleDown.TrickleDown);
        }

        private void RequestConnectedFact()
        {
            var port = _dragSourcePort;
            _dragSourcePort = null;
            RequestConnectedFact(port, _lastPointerPosition);
        }

        private void RequestConnectedFact(Port port, Vector2 position)
        {
            if (port?.node?.userData is not GoapDefinition owner)
            {
                return;
            }

            var role = owner switch
            {
                GoapActionDefinition when port.name == "preconditions" => GoapGraphConnectionRole.ActionPrecondition,
                GoapActionDefinition when port.name == "effects" => GoapGraphConnectionRole.ActionEffect,
                GoapGoalDefinition when port.name == "activation" => GoapGraphConnectionRole.GoalActivation,
                GoapGoalDefinition when port.name == "desired" => GoapGraphConnectionRole.GoalDesired,
                _ => (GoapGraphConnectionRole?)null
            };
            if (role.HasValue)
            {
                ConnectedFactRequested?.Invoke(owner, role.Value, position);
            }
        }

        private bool CommitEdge(Edge edge)
        {
            if (!TryCreateBinding(edge, out var binding) || !AddBinding(binding))
            {
                return false;
            }

            edge.userData = binding;
            edge.input.Connect(edge);
            edge.output.Connect(edge);
            AddElement(edge);
            schedule.Execute(() => Rebuild(_domain, _selectionChanged));
            return true;
        }

        private void AddAnnotations()
        {
            foreach (var layout in _domain.GraphGroups.Where(item => item != null))
            {
                var group = new Group { title = layout.Title, userData = layout };
                group.SetPosition(layout.Rect);
                AddElement(group);
                foreach (var memberId in layout.MemberIds)
                {
                    var member = _nodes.FirstOrDefault(pair => pair.Key.Id == memberId).Value;
                    if (member != null)
                    {
                        group.AddElement(member);
                    }
                }

                group.RegisterCallback<FocusOutEvent>(_ => SaveGroup(group));
            }

            foreach (var layout in _domain.GraphNotes.Where(item => item != null))
            {
                var note = new StickyNote
                {
                    title = layout.Title,
                    contents = layout.Contents,
                    userData = layout
                };
                note.SetPosition(layout.Rect);
                note.RegisterCallback<FocusOutEvent>(_ => SaveNote(note));
                AddElement(note);
            }
        }

        private void CreateGroup(Vector2 position)
        {
            var members = selection.OfType<Node>().ToArray();
            var rect = members.Length == 0
                ? new Rect(position, new Vector2(360f, 240f))
                : ExpandRect(GetBounds(members), 35f);
            Undo.RecordObject(_domain, "Create GOAP Group");
            _domain.AddGraphGroup(
                "Group",
                rect,
                members.Select(node => (node.userData as GoapDefinition)?.Id).Where(id => id != null));
            EditorUtility.SetDirty(_domain);
            Rebuild(_domain, _selectionChanged);
        }

        private void CreateNote(Vector2 position)
        {
            Undo.RecordObject(_domain, "Create GOAP Note");
            _domain.AddGraphNote("Note", "", new Rect(position, new Vector2(240f, 160f)));
            EditorUtility.SetDirty(_domain);
            Rebuild(_domain, _selectionChanged);
        }

        private void SaveGroup(Group group)
        {
            if (_rebuilding || _domain == null || group?.userData is not GoapGraphGroupLayout layout)
            {
                return;
            }

            Undo.RecordObject(_domain, "Edit GOAP Group");
            var memberIds = group.containedElements
                .OfType<Node>()
                .Select(node => (node.userData as GoapDefinition)?.Id)
                .Where(id => id != null);
            layout.Update(group.title, group.GetPosition(), memberIds);
            EditorUtility.SetDirty(_domain);
        }

        private void SaveNote(StickyNote note)
        {
            if (_rebuilding || _domain == null || note?.userData is not GoapGraphNoteLayout layout)
            {
                return;
            }

            Undo.RecordObject(_domain, "Edit GOAP Note");
            layout.Update(note.title, note.contents, note.GetPosition());
            EditorUtility.SetDirty(_domain);
        }

        private static Rect GetBounds(IReadOnlyList<Node> nodes)
        {
            var min = nodes[0].GetPosition().min;
            var max = nodes[0].GetPosition().max;
            for (var index = 1; index < nodes.Count; index++)
            {
                min = Vector2.Min(min, nodes[index].GetPosition().min);
                max = Vector2.Max(max, nodes[index].GetPosition().max);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect ExpandRect(Rect rect, float amount)
        {
            return Rect.MinMaxRect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.xMax + amount,
                rect.yMax + amount);
        }

        private bool AddBinding(GoapGraphEdgeBinding binding)
        {
            if (binding?.Fact == null || binding.Owner == null)
            {
                return false;
            }

            Undo.RecordObject(binding.Owner, "Connect GOAP Fact");
            var condition = CreateDefaultCondition(binding.Fact);
            var changed = binding.Kind switch
            {
                GoapGraphBindingKind.ActionPrecondition when binding.Owner is GoapActionDefinition action =>
                    action.AddPrecondition(condition),
                GoapGraphBindingKind.ActionEffect when binding.Owner is GoapActionDefinition action =>
                    action.AddEffect(condition),
                GoapGraphBindingKind.GoalActivation when binding.Owner is GoapGoalDefinition goal =>
                    goal.AddActivationCondition(condition),
                GoapGraphBindingKind.GoalDesired when binding.Owner is GoapGoalDefinition goal =>
                    goal.AddDesiredCondition(condition),
                _ => false
            };
            if (changed)
            {
                EditorUtility.SetDirty(binding.Owner);
            }

            return changed;
        }

        private void RemoveBinding(GoapGraphEdgeBinding binding)
        {
            if (binding?.Fact == null || binding.Owner == null)
            {
                return;
            }

            Undo.RecordObject(binding.Owner, "Disconnect GOAP Fact");
            var changed = binding.Kind switch
            {
                GoapGraphBindingKind.ActionPrecondition when binding.Owner is GoapActionDefinition action =>
                    action.RemovePrecondition(binding.Fact),
                GoapGraphBindingKind.ActionEffect when binding.Owner is GoapActionDefinition action =>
                    action.RemoveEffect(binding.Fact),
                GoapGraphBindingKind.GoalActivation when binding.Owner is GoapGoalDefinition goal =>
                    goal.RemoveActivationCondition(binding.Fact),
                GoapGraphBindingKind.GoalDesired when binding.Owner is GoapGoalDefinition goal =>
                    goal.RemoveDesiredCondition(binding.Fact),
                _ => false
            };
            if (changed)
            {
                EditorUtility.SetDirty(binding.Owner);
            }
        }

        private static bool TryCreateBinding(Edge edge, out GoapGraphEdgeBinding binding)
        {
            binding = null;
            if (edge?.output?.node?.userData is GoapFact fact &&
                edge.input?.node?.userData is GoapDefinition reader)
            {
                var kind = edge.input.name switch
                {
                    "preconditions" => GoapGraphBindingKind.ActionPrecondition,
                    "activation" => GoapGraphBindingKind.GoalActivation,
                    "desired" => GoapGraphBindingKind.GoalDesired,
                    _ => GoapGraphBindingKind.Invalid
                };
                if (kind != GoapGraphBindingKind.Invalid)
                {
                    binding = new GoapGraphEdgeBinding(fact, reader, kind);
                    return true;
                }
            }

            if (edge?.output?.node?.userData is GoapActionDefinition action &&
                edge.input?.node?.userData is GoapFact writtenFact &&
                edge.output.name == "effects" && edge.input.name == "setters")
            {
                binding = new GoapGraphEdgeBinding(writtenFact, action, GoapGraphBindingKind.ActionEffect);
                return true;
            }

            return false;
        }

        private static bool IsCompatible(Port first, Port second)
        {
            if (first.direction == second.direction)
            {
                return false;
            }

            var output = first.direction == Direction.Output ? first : second;
            var input = first.direction == Direction.Input ? first : second;
            if (output.node.userData is GoapFact)
            {
                return input.node.userData is GoapActionDefinition && input.name == "preconditions" ||
                       input.node.userData is GoapGoalDefinition &&
                       (input.name == "activation" || input.name == "desired");
            }

            return output.node.userData is GoapActionDefinition &&
                   output.name == "effects" &&
                   input.node.userData is GoapFact &&
                   input.name == "setters";
        }

        private static GoapCondition CreateDefaultCondition(GoapFact fact)
        {
            return fact.ValueType switch
            {
                GoapFactType.Integer => new GoapCondition(fact, fact.DefaultTypedValue.Integer),
                GoapFactType.Float => new GoapCondition(fact, fact.DefaultTypedValue.Float),
                GoapFactType.Enum => new GoapCondition(fact, fact.DefaultTypedValue.Integer),
                _ => new GoapCondition(fact, true)
            };
        }

        private void AlignSelection(bool left)
        {
            var nodes = selection.OfType<Node>().ToArray();
            if (nodes.Length < 2 || _domain == null)
            {
                return;
            }

            Undo.RecordObject(_domain, left ? "Align GOAP Nodes Left" : "Align GOAP Nodes Top");
            var coordinate = left
                ? nodes.Min(node => node.GetPosition().x)
                : nodes.Min(node => node.GetPosition().y);
            foreach (var node in nodes)
            {
                var rect = node.GetPosition();
                node.SetPosition(left
                    ? new Rect(coordinate, rect.y, rect.width, rect.height)
                    : new Rect(rect.x, coordinate, rect.width, rect.height));
                StoreNodePosition(node);
            }

            EditorUtility.SetDirty(_domain);
        }

        private void DistributeSelection(bool horizontal)
        {
            var nodes = selection.OfType<Node>()
                .OrderBy(node => horizontal ? node.GetPosition().x : node.GetPosition().y)
                .ToArray();
            if (nodes.Length < 3 || _domain == null)
            {
                return;
            }

            Undo.RecordObject(_domain, "Distribute GOAP Nodes");
            var first = horizontal ? nodes[0].GetPosition().x : nodes[0].GetPosition().y;
            var last = horizontal ? nodes[^1].GetPosition().x : nodes[^1].GetPosition().y;
            var spacing = (last - first) / (nodes.Length - 1);
            for (var index = 1; index < nodes.Length - 1; index++)
            {
                var rect = nodes[index].GetPosition();
                nodes[index].SetPosition(horizontal
                    ? new Rect(first + spacing * index, rect.y, rect.width, rect.height)
                    : new Rect(rect.x, first + spacing * index, rect.width, rect.height));
                StoreNodePosition(nodes[index]);
            }

            EditorUtility.SetDirty(_domain);
        }

        private void StoreNodePosition(Node element)
        {
            if (element.userData is GoapDefinition definition)
            {
                StoreDefinitionPosition(definition, element.GetPosition().position);
            }
        }

        private void StoreDefinitionPosition(GoapDefinition definition, Vector2 position)
        {
            switch (definition)
            {
                case GoapActionDefinition action:
                    _domain.SetNodePosition(action.Id, GoapNodeKind.Action, position);
                    break;
                case GoapGoalDefinition goal:
                    _domain.SetNodePosition(goal.Id, GoapNodeKind.Goal, position);
                    break;
                case GoapFact fact:
                    _domain.SetNodePosition(fact.Id, GoapNodeKind.Fact, position);
                    break;
            }
        }

        private static VisualElement CreateMetaLabel(string text)
        {
            return new Label(text)
            {
                style =
                {
                    color = new Color(0.68f, 0.72f, 0.76f),
                    fontSize = 10,
                    marginBottom = 5f
                }
            };
        }

        private static VisualElement CreateConditionBlock(
            string title,
            IReadOnlyList<GoapCondition> conditions,
            Color color)
        {
            var container = new VisualElement
            {
                style =
                {
                    marginTop = 3f,
                    paddingLeft = 6f,
                    borderLeftWidth = 2f,
                    borderLeftColor = color
                }
            };
            container.Add(new Label(title)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = color,
                    fontSize = 10
                }
            });

            if (conditions.Count == 0)
            {
                container.Add(new Label("None") { style = { opacity = 0.55f, fontSize = 10 } });
            }
            else
            {
                foreach (var condition in conditions)
                {
                    container.Add(new Label(condition.ToString()) { style = { fontSize = 10 } });
                }
            }

            return container;
        }

        private static Color GetDefaultColor(GoapDefinition definition)
        {
            if (definition.HasCustomNodeColor)
            {
                return definition.NodeColor;
            }

            return definition switch
            {
                GoapGoalDefinition _ => new Color(0.32f, 0.62f, 0.88f),
                GoapFact _ => new Color(0.2f, 0.62f, 0.65f),
                _ => new Color(0.35f, 0.39f, 0.43f)
            };
        }

        private Color GetBaseColor(GoapDefinition definition)
        {
            if (!_validation.TryGetValue(definition, out var severity))
            {
                return GetDefaultColor(definition);
            }

            return severity switch
            {
                GoapValidationSeverity.Error => new Color(0.95f, 0.25f, 0.22f),
                GoapValidationSeverity.Warning => new Color(0.96f, 0.68f, 0.18f),
                _ => new Color(0.3f, 0.68f, 0.95f)
            };
        }

        private GoapDomain GetRuntimeDomain()
        {
            return _runtimeSnapshot != null ? _runtimeSnapshot.Domain : _runtimeAgent?.Domain;
        }

        private GoapGoalDefinition GetRuntimeGoal()
        {
            return _runtimeSnapshot != null ? _runtimeSnapshot.Goal : _runtimeAgent?.CurrentGoal;
        }

        private GoapActionDefinition GetRuntimeAction()
        {
            return _runtimeSnapshot != null ? _runtimeSnapshot.Action : _runtimeAgent?.CurrentAction;
        }

        private IReadOnlyList<GoapActionDefinition> GetRuntimePlanActions()
        {
            if (_runtimeSnapshot != null)
            {
                return _runtimeSnapshot.PlanActions;
            }

            if (_runtimeAgent == null)
            {
                return Array.Empty<GoapActionDefinition>();
            }

            var result = new List<GoapActionDefinition>();
            if (_runtimeAgent.CurrentAction != null)
            {
                result.Add(_runtimeAgent.CurrentAction);
            }

            result.AddRange(_runtimeAgent.PendingActions);
            return result;
        }

        private IReadOnlyList<GoapActionDiagnostic> GetRuntimeActionDiagnostics()
        {
            return _runtimeSnapshot != null
                ? _runtimeSnapshot.ActionDiagnostics
                : _runtimeAgent?.ActionDiagnostics ?? Array.Empty<GoapActionDiagnostic>();
        }

        private IReadOnlyList<GoapGoalDiagnostic> GetRuntimeGoalDiagnostics()
        {
            return _runtimeSnapshot != null
                ? _runtimeSnapshot.GoalDiagnostics
                : _runtimeAgent?.GoalDiagnostics ?? Array.Empty<GoapGoalDiagnostic>();
        }

        private GoapValue GetRuntimeValue(GoapFact fact)
        {
            return _runtimeSnapshot != null
                ? _runtimeSnapshot.GetValue(fact)
                : _runtimeAgent?.WorldState?.GetValue(fact) ?? fact.DefaultTypedValue;
        }

        private static string BuildRuntimeTooltip(string description, string runtimeDetail)
        {
            return string.IsNullOrWhiteSpace(description)
                ? $"Runtime: {runtimeDetail}"
                : $"{description}\n\nRuntime: {runtimeDetail}";
        }

        private static string GetActionRuntimeDetail(GoapActionDiagnostic diagnostic)
        {
            if (!diagnostic.HasExecutor)
            {
                return "No matching executor";
            }

            var unmet = diagnostic.Preconditions
                .Where(condition => !condition.Satisfied)
                .Select(condition => condition.Reason)
                .ToArray();
            return unmet.Length == 0 ? "Ready now" : string.Join("\n", unmet);
        }

        private static void SetRuntimeBadge(
            Node node,
            string text,
            Color color,
            string tooltip)
        {
            var badge = node.Q<Label>("runtime-badge");
            if (badge == null)
            {
                badge = new Label { name = "runtime-badge" };
                badge.style.fontSize = 9;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.marginLeft = 5f;
                node.titleContainer.Add(badge);
            }

            badge.text = text;
            badge.tooltip = tooltip;
            badge.style.color = color;
            badge.style.display = DisplayStyle.Flex;
        }

        private static void SetNodeAccent(Node node, Color color, float width = 2f)
        {
            node.style.borderTopColor = color;
            node.style.borderRightColor = color;
            node.style.borderBottomColor = color;
            node.style.borderLeftColor = color;
            node.style.borderTopWidth = width;
            node.style.borderRightWidth = width;
            node.style.borderBottomWidth = width;
            node.style.borderLeftWidth = width;
        }

        private void ApplyDetailsState()
        {
            foreach (var node in _nodes.Values)
            {
                node.expanded = _showDetails;
                node.RefreshExpandedState();
            }
        }

        private void RefreshGraphVisuals()
        {
            if (_rebuilding)
            {
                return;
            }

            var focusSet = _focusMode && _focusedDefinition != null
                ? BuildFocusSet(_focusedDefinition)
                : null;
            foreach (var pair in _nodes)
            {
                var matchesSearch = MatchesSearch(pair.Key);
                var matchesFocus = focusSet == null || focusSet.Contains(pair.Key);
                pair.Value.style.opacity = !matchesSearch ? 0.08f : matchesFocus ? 1f : 0.12f;
            }

            foreach (var edge in edges)
            {
                if (edge.userData is not GoapGraphEdgeBinding binding)
                {
                    continue;
                }

                var visible = IsConnectionVisible(binding.Kind);
                edge.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    continue;
                }

                var matchesFocus = focusSet == null ||
                                   focusSet.Contains(binding.Fact) && focusSet.Contains(binding.Owner);
                var matchesSearch = string.IsNullOrWhiteSpace(_searchQuery) ||
                                    MatchesSearch(binding.Fact) || MatchesSearch(binding.Owner);
                var emphasized = matchesFocus && matchesSearch && focusSet != null;
                edge.style.opacity = !matchesSearch ? 0.03f : matchesFocus ? focusSet == null ? 0.46f : 0.96f : 0.035f;
                ApplyEdgeStyle(edge, binding, emphasized);
            }
        }

        private HashSet<GoapDefinition> BuildFocusSet(GoapDefinition definition)
        {
            var result = new HashSet<GoapDefinition> { definition };
            switch (definition)
            {
                case GoapGoalDefinition goal:
                    var visitedFacts = new HashSet<GoapFact>();
                    foreach (var fact in goal.ActivationConditions.Concat(goal.DesiredState)
                                 .Select(condition => condition.Fact)
                                 .Where(fact => fact != null))
                    {
                        result.Add(fact);
                        AddFactProducers(fact, result, visitedFacts, 8);
                    }
                    break;

                case GoapActionDefinition action:
                    var relatedFacts = action.Preconditions.Concat(action.Effects)
                        .Select(condition => condition.Fact)
                        .Where(fact => fact != null)
                        .Distinct()
                        .ToArray();
                    foreach (var fact in relatedFacts)
                    {
                        result.Add(fact);
                    }

                    foreach (var relatedGoal in _domain.Goals.Where(goal => goal != null &&
                                 goal.ActivationConditions.Concat(goal.DesiredState)
                                     .Any(condition => condition.Fact != null && relatedFacts.Contains(condition.Fact))))
                    {
                        result.Add(relatedGoal);
                    }
                    break;

                case GoapFact fact:
                    foreach (var relatedAction in _domain.Actions.Where(action => action != null &&
                                 action.Preconditions.Concat(action.Effects)
                                     .Any(condition => condition.Fact == fact)))
                    {
                        result.Add(relatedAction);
                    }

                    foreach (var relatedGoal in _domain.Goals.Where(goal => goal != null &&
                                 goal.ActivationConditions.Concat(goal.DesiredState)
                                     .Any(condition => condition.Fact == fact)))
                    {
                        result.Add(relatedGoal);
                    }
                    break;
            }

            return result;
        }

        private void AddFactProducers(
            GoapFact fact,
            ISet<GoapDefinition> result,
            ISet<GoapFact> visitedFacts,
            int remainingDepth)
        {
            if (fact == null || remainingDepth <= 0 || !visitedFacts.Add(fact))
            {
                return;
            }

            foreach (var action in _domain.Actions.Where(action => action != null &&
                         action.Effects.Any(effect => effect.Fact == fact)))
            {
                result.Add(action);
                foreach (var effectFact in action.Effects.Select(effect => effect.Fact).Where(item => item != null))
                {
                    result.Add(effectFact);
                }

                foreach (var preconditionFact in action.Preconditions
                             .Select(condition => condition.Fact)
                             .Where(item => item != null))
                {
                    result.Add(preconditionFact);
                    AddFactProducers(preconditionFact, result, visitedFacts, remainingDepth - 1);
                }
            }
        }

        private bool MatchesSearch(GoapDefinition definition)
        {
            return definition != null &&
                   (string.IsNullOrWhiteSpace(_searchQuery) ||
                    definition.DisplayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrWhiteSpace(definition.Description) &&
                     definition.Description.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private bool IsConnectionVisible(GoapGraphBindingKind kind)
        {
            return kind switch
            {
                GoapGraphBindingKind.ActionPrecondition => _showPreconditions,
                GoapGraphBindingKind.ActionEffect => _showEffects,
                GoapGraphBindingKind.GoalActivation => _showGoalLinks,
                GoapGraphBindingKind.GoalDesired => _showGoalLinks,
                _ => true
            };
        }

        private static void ApplyEdgeStyle(Edge edge, GoapGraphEdgeBinding binding, bool emphasized)
        {
            if (edge?.edgeControl == null || binding == null)
            {
                return;
            }

            var color = binding.Kind switch
            {
                GoapGraphBindingKind.ActionPrecondition => new Color(0.95f, 0.67f, 0.2f),
                GoapGraphBindingKind.ActionEffect => new Color(0.3f, 0.82f, 0.5f),
                GoapGraphBindingKind.GoalActivation => new Color(0.76f, 0.48f, 0.9f),
                GoapGraphBindingKind.GoalDesired => new Color(0.3f, 0.68f, 1f),
                _ => new Color(0.72f, 0.75f, 0.78f)
            };
            edge.edgeControl.inputColor = color;
            edge.edgeControl.outputColor = color;
            edge.edgeControl.edgeWidth = emphasized ? 3 : 2;
            edge.edgeControl.drawToCap = true;
            edge.edgeControl.toCapColor = color;
            edge.edgeControl.capRadius = emphasized ? 4f : 3f;
            edge.edgeControl.MarkDirtyRepaint();
        }

        private static void ApplyRuntimeEdgeStyle(Edge edge, Color color)
        {
            if (edge?.edgeControl == null)
            {
                return;
            }

            edge.style.opacity = 1f;
            edge.edgeControl.inputColor = color;
            edge.edgeControl.outputColor = color;
            edge.edgeControl.edgeWidth = 4;
            edge.edgeControl.drawToCap = true;
            edge.edgeControl.toCapColor = color;
            edge.edgeControl.capRadius = 4f;
            edge.edgeControl.MarkDirtyRepaint();
        }

        private static Vector2 GetCompactNodeSize(GoapDefinition definition, float width)
        {
            var height = definition switch
            {
                GoapFact _ => 76f,
                GoapGoalDefinition _ => 104f,
                _ => 86f
            };
            return new Vector2(width, height);
        }

        private enum GoapGraphBindingKind
        {
            Invalid,
            ActionPrecondition,
            ActionEffect,
            GoalActivation,
            GoalDesired
        }

        private sealed class GoapGraphEdgeBinding
        {
            public GoapFact Fact { get; }
            public GoapDefinition Owner { get; }
            public GoapGraphBindingKind Kind { get; }

            public GoapGraphEdgeBinding(GoapFact fact, GoapDefinition owner, GoapGraphBindingKind kind)
            {
                Fact = fact;
                Owner = owner;
                Kind = kind;
            }
        }

        private sealed class GoapPort : Port
        {
            public GoapPort(
                Orientation orientation,
                Direction direction,
                Capacity capacity,
                Type type,
                IEdgeConnectorListener listener)
                : base(orientation, direction, capacity, type)
            {
                m_EdgeConnector = new EdgeConnector<Edge>(listener);
                this.AddManipulator(m_EdgeConnector);
            }
        }

        private sealed class GoapEdgeConnectorListener : IEdgeConnectorListener
        {
            private readonly GoapGraphView _view;

            public GoapEdgeConnectorListener(GoapGraphView view)
            {
                _view = view;
            }

            public void OnDrop(GraphView graphView, Edge edge)
            {
                _view.CommitEdge(edge);
            }

            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                var sourcePort = edge.output ?? edge.input;
                var graphPosition = _view.contentViewContainer.WorldToLocal(position);
                _view.RequestConnectedFact(sourcePort, graphPosition);
            }
        }
    }
}
