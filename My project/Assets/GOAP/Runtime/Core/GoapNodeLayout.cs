using System;
using UnityEngine;

namespace Practice.GOAP
{
    public enum GoapNodeKind
    {
        Action,
        Goal,
        Fact
    }

    [Serializable]
    public struct GoapNodeLayout
    {
        [SerializeField] private string _definitionId;
        [SerializeField] private GoapNodeKind _kind;
        [SerializeField] private Vector2 _position;

        public string DefinitionId => _definitionId;
        public GoapNodeKind Kind => _kind;
        public Vector2 Position => _position;

        public GoapNodeLayout(string definitionId, GoapNodeKind kind, Vector2 position)
        {
            _definitionId = definitionId;
            _kind = kind;
            _position = position;
        }
    }
}
