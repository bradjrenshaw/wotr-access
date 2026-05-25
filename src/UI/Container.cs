using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// A passive structural blueprint: holds children, remembers its focused child
    /// (for restore), and exposes shape geometry. Navigation/input policy lives in
    /// the Navigator. See <see cref="Panel"/> (Tab-traversed) and
    /// <see cref="ListContainer"/> (arrow-traversed, single Tab-stop).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement))]
    public class Container : UIElement
    {
        private readonly List<UIElement> _children = new List<UIElement>();
        private string _label;

        public IReadOnlyList<UIElement> Children => _children;

        /// <summary>Remembered focus within this container, for restore on re-entry.</summary>
        public UIElement FocusedChild { get; private set; }

        public ContainerShape Shape { get; protected set; } = ContainerShape.VerticalList;

        /// <summary>When set on a screen root, navigators that respect it wrap Tab past the ends.</summary>
        public bool Wrap { get; set; }

        public override string Label => _label;

        /// <summary>Lists/grids announce their children's position; panels (pure structure) don't.</summary>
        public virtual bool AnnouncePosition => Shape != ContainerShape.Panel;

        /// <summary>"index of count" among focusable children, or null if not found.</summary>
        public Message GetPositionString(UIElement child)
        {
            int count = 0, ci = -1;
            for (int i = 0; i < _children.Count; i++)
            {
                if (!_children[i].CanFocus) continue;
                count++;
                if (_children[i] == child) ci = count;
            }
            return ci < 0 ? null : Message.Raw(ci + " of " + count);
        }

        // Unlabeled structural containers stay silent; labeled ones announce name (+ "list" for lists).
        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (string.IsNullOrEmpty(Label)) yield break;
            yield return new LabelAnnouncement(Message.Raw(Label));
            if (Shape != ContainerShape.Panel) yield return new RoleAnnouncement("list");
        }

        public Container() { }

        public Container(ContainerShape shape, string label = null)
        {
            Shape = shape;
            _label = label;
        }

        protected void SetContainerLabel(string label) => _label = label;

        public void Add(UIElement element)
        {
            element.Parent = this;
            _children.Add(element);
        }

        public void Clear()
        {
            _children.Clear();
            FocusedChild = null;
        }

        public void SetFocusedChild(UIElement element) => FocusedChild = element;

        /// <summary>First child the navigator may land on (skips non-focusable).</summary>
        public virtual UIElement FirstFocusable()
        {
            for (int i = 0; i < _children.Count; i++)
                if (_children[i].CanFocus) return _children[i];
            return null;
        }

        /// <summary>Next focusable child from <paramref name="from"/> in a direction (list shapes only).</summary>
        public virtual UIElement GetNeighbor(UIElement from, NavDirection dir)
        {
            int step = StepFor(dir);
            if (step == 0) return null;
            int idx = _children.IndexOf(from);
            if (idx < 0) return null;
            for (int i = idx + step; i >= 0 && i < _children.Count; i += step)
                if (_children[i].CanFocus) return _children[i];
            return null;
        }

        private int StepFor(NavDirection dir)
        {
            if (Shape == ContainerShape.VerticalList)
                return dir == NavDirection.Down ? 1 : dir == NavDirection.Up ? -1 : 0;
            if (Shape == ContainerShape.HorizontalList)
                return dir == NavDirection.Right ? 1 : dir == NavDirection.Left ? -1 : 0;
            return 0; // Panel uses Tab traversal; Grid overrides.
        }
    }
}
