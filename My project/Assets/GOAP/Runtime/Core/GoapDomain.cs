using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Practice.GOAP
{
    [CreateAssetMenu(menuName = "GOAP/Domain", fileName = "New GOAP Domain")]
    public sealed class GoapDomain : ScriptableObject
    {
        [SerializeField] private List<GoapFact> _facts = new();
        [SerializeField] private List<GoapActionDefinition> _actions = new();
        [SerializeField] private List<GoapGoalDefinition> _goals = new();
        [SerializeField, HideInInspector] private List<GoapNodeLayout> _nodeLayout = new();
        [SerializeField, HideInInspector] private List<GoapGraphGroupLayout> _graphGroups = new();
        [SerializeField, HideInInspector] private List<GoapGraphNoteLayout> _graphNotes = new();

        private GoapCompiledDomain _compiledDomain;

        public IReadOnlyList<GoapFact> Facts => _facts;
        public IReadOnlyList<GoapActionDefinition> Actions => _actions;
        public IReadOnlyList<GoapGoalDefinition> Goals => _goals;
        public IReadOnlyList<GoapGraphGroupLayout> GraphGroups => _graphGroups;
        public IReadOnlyList<GoapGraphNoteLayout> GraphNotes => _graphNotes;

        public GoapFact FindFact(string displayName)
        {
            return _facts.FirstOrDefault(fact => fact != null && fact.DisplayName == displayName);
        }

        public GoapActionDefinition FindAction(string executorId)
        {
            return _actions.FirstOrDefault(action => action != null && action.ExecutorId == executorId);
        }

        public void AddFact(GoapFact fact)
        {
            if (fact != null && !_facts.Contains(fact))
            {
                _facts.Add(fact);
                _compiledDomain = null;
            }
        }

        public void AddAction(GoapActionDefinition action)
        {
            if (action != null && !_actions.Contains(action))
            {
                _actions.Add(action);
                _compiledDomain = null;
            }
        }

        public void AddGoal(GoapGoalDefinition goal)
        {
            if (goal != null && !_goals.Contains(goal))
            {
                _goals.Add(goal);
                _compiledDomain = null;
            }
        }

        public void Remove(GoapDefinition definition)
        {
            switch (definition)
            {
                case GoapFact fact:
                    _facts.Remove(fact);
                    break;
                case GoapActionDefinition action:
                    _actions.Remove(action);
                    break;
                case GoapGoalDefinition goal:
                    _goals.Remove(goal);
                    break;
            }

            _compiledDomain = null;
        }

        public bool TryGetNodePosition(string definitionId, GoapNodeKind kind, out Vector2 position)
        {
            foreach (var entry in _nodeLayout)
            {
                if (entry.DefinitionId == definitionId && entry.Kind == kind)
                {
                    position = entry.Position;
                    return true;
                }
            }

            position = default;
            return false;
        }

        public void SetNodePosition(string definitionId, GoapNodeKind kind, Vector2 position)
        {
            for (var index = 0; index < _nodeLayout.Count; index++)
            {
                var entry = _nodeLayout[index];
                if (entry.DefinitionId == definitionId && entry.Kind == kind)
                {
                    _nodeLayout[index] = new GoapNodeLayout(definitionId, kind, position);
                    return;
                }
            }

            _nodeLayout.Add(new GoapNodeLayout(definitionId, kind, position));
        }

        public GoapGraphGroupLayout AddGraphGroup(string title, Rect rect, IEnumerable<string> memberIds)
        {
            var group = new GoapGraphGroupLayout(title, rect, memberIds);
            _graphGroups.Add(group);
            return group;
        }

        public void RemoveGraphGroup(string id)
        {
            _graphGroups.RemoveAll(group => group != null && group.Id == id);
        }

        public GoapGraphNoteLayout AddGraphNote(string title, string contents, Rect rect)
        {
            var note = new GoapGraphNoteLayout(title, contents, rect);
            _graphNotes.Add(note);
            return note;
        }

        public void RemoveGraphNote(string id)
        {
            _graphNotes.RemoveAll(note => note != null && note.Id == id);
        }

        public GoapWorldState CreateDefaultState()
        {
            return Compile().CreateDefaultState();
        }

        public GoapCompiledDomain Compile()
        {
            return _compiledDomain ??= new GoapCompiledDomain(this);
        }
    }
}
