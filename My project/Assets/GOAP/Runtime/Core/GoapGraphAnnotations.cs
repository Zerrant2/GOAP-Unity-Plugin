using System;
using System.Collections.Generic;
using UnityEngine;

namespace Practice.GOAP
{
    [Serializable]
    public sealed class GoapGraphGroupLayout
    {
        [SerializeField] private string _id;
        [SerializeField] private string _title;
        [SerializeField] private Rect _rect;
        [SerializeField] private List<string> _memberIds = new();

        public string Id => _id;
        public string Title => _title;
        public Rect Rect => _rect;
        public IReadOnlyList<string> MemberIds => _memberIds;

        public GoapGraphGroupLayout(string title, Rect rect, IEnumerable<string> memberIds)
        {
            _id = Guid.NewGuid().ToString("N");
            Update(title, rect, memberIds);
        }

        public void Update(string title, Rect rect, IEnumerable<string> memberIds)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Group" : title;
            _rect = rect;
            _memberIds = memberIds == null ? new List<string>() : new List<string>(memberIds);
        }
    }

    [Serializable]
    public sealed class GoapGraphNoteLayout
    {
        [SerializeField] private string _id;
        [SerializeField] private string _title;
        [SerializeField, TextArea] private string _contents;
        [SerializeField] private Rect _rect;

        public string Id => _id;
        public string Title => _title;
        public string Contents => _contents;
        public Rect Rect => _rect;

        public GoapGraphNoteLayout(string title, string contents, Rect rect)
        {
            _id = Guid.NewGuid().ToString("N");
            Update(title, contents, rect);
        }

        public void Update(string title, string contents, Rect rect)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Note" : title;
            _contents = contents ?? string.Empty;
            _rect = rect;
        }
    }
}
