using System;
using UnityEngine;

namespace Practice.GOAP
{
    public abstract class GoapDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;
        [SerializeField] private string _category = string.Empty;
        [SerializeField] private Texture2D _icon = null;
        [SerializeField] private Color _nodeColor = Color.clear;

        public string Id => _id;
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
        public string Description => _description;
        public string Category => _category;
        public Texture2D Icon => _icon;
        public bool HasCustomNodeColor => _nodeColor.a > 0.01f;
        public Color NodeColor => _nodeColor;

        public void SetIdentity(string displayName, string description = "")
        {
            EnsureId();
            _displayName = displayName;
            _description = description;
            name = displayName;
        }

        protected virtual void OnValidate()
        {
            EnsureId();
        }

        private void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                _id = Guid.NewGuid().ToString("N");
            }
        }
    }
}
