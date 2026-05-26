using System;
using System.Collections.Generic;
using System.Linq;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// One node in a tooltip tree (the structured replacement for the old flat brick element list).
    /// A node is a Tree-shaped <see cref="Container"/> so it reuses the existing focus-path / parent
    /// chain / announcement machinery. Two ways children arrive:
    ///  • <b>eager</b> — a builder group whose children are <see cref="Container.Add"/>ed as the brick
    ///    stream is walked (known up front);
    ///  • <b>lazy</b> — a drill-in node (a feature write-up, a glossary link, a nested tooltip) whose
    ///    children are materialized only on first <see cref="Expand"/>. Lazy is required: nested
    ///    tooltips can be deep or even cyclic, so we don't build a subtree until entered.
    /// A leaf has neither. Built by <see cref="TooltipTreeBuilder"/> (title H1–H6 ranks form groups);
    /// per-brick shape comes from the renderers' <c>GetNodes</c>.
    /// </summary>
    public sealed class TooltipNode : Container
    {
        private readonly string _label;
        private readonly Func<IEnumerable<TooltipNode>> _childFactory; // lazy children (drill-in), else null
        private bool _built;

        /// <summary>Optional state suffix read after the label (e.g. "added"/"removed" for an
        /// archetype feature) — distinct from expand/collapse, which the node reports itself.</summary>
        public string Annotation { get; }

        public override string Label => _label;
        public override string Role { get; }

        private TooltipNode(string label, string role, string annotation,
            Func<IEnumerable<TooltipNode>> childFactory)
            : base(ContainerShape.Tree, label)
        {
            _label = label;
            Role = role;
            Annotation = annotation;
            _childFactory = childFactory;
        }

        /// <summary>A leaf — a single read-out line, optionally carrying a drill-in tooltip whose
        /// content becomes this node's (lazy) children when expanded.</summary>
        public static TooltipNode Leaf(string label, string role = null, string annotation = null,
            TooltipBaseTemplate drillIn = null)
        {
            Func<IEnumerable<TooltipNode>> factory = drillIn == null
                ? null
                : (Func<IEnumerable<TooltipNode>>)(() => TooltipTreeBuilder.Build(drillIn));
            return new TooltipNode(label, role, annotation, factory);
        }

        /// <summary>An empty group whose children the builder <see cref="Container.Add"/>s eagerly.</summary>
        public static TooltipNode Branch(string label, string role = "group")
            => new TooltipNode(label, role, null, null);

        /// <summary>A group whose children are supplied eagerly.</summary>
        public static TooltipNode Group(string label, IEnumerable<TooltipNode> children, string role = "group")
        {
            var node = new TooltipNode(label, role, null, null);
            foreach (var c in children ?? Enumerable.Empty<TooltipNode>())
                if (c != null) node.Add(c);
            return node;
        }

        /// <summary>A drill-in/group node whose children are built on first expand.</summary>
        public static TooltipNode Lazy(string label, Func<IEnumerable<TooltipNode>> childFactory,
            string role = "group", string annotation = null)
            => new TooltipNode(label, role, annotation, childFactory);

        /// <summary>Can be expanded — eager children present, or a lazy drill-in not yet run. A
        /// drill-in is suppressed when an ancestor already has the same label: feature tooltips embed
        /// the feature itself (same drill-in) as their header, so without this a node would re-open
        /// itself forever (e.g. Sneak Attack → Sneak Attack → …). (Cheap: doesn't build children.)</summary>
        public override bool Expandable
        {
            get
            {
                if (Children.Count > 0) return true;       // eager group / already-built drill-in
                if (_childFactory == null) return false;   // plain leaf
                return !HasAncestorLabel(_label);          // lazy drill-in, unless it would cycle
            }
        }

        private bool HasAncestorLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            for (var p = Parent; p != null; p = p.Parent)
                if (string.Equals(p.Label, label)) return true;
            return false;
        }

        /// <summary>Build lazy children on first expand (cached); then mark expanded. No-op for leaves.</summary>
        public override void Expand()
        {
            if (!Expandable) return;
            if (!_built && _childFactory != null)
            {
                _built = true;
                foreach (var child in _childFactory() ?? Enumerable.Empty<TooltipNode>())
                {
                    if (child == null) continue;
                    // A drill-in's content is usually fronted by a header repeating this node's label
                    // (a glossary's title, a feature's name). Don't keep that redundant header — but if
                    // it's a GROUP, the real content is nested inside it, so splice in its children;
                    // only a bare leaf header is dropped outright.
                    if (string.Equals(child.Label, _label))
                        foreach (var grandchild in child.Children) Add(grandchild);
                    else
                        Add(child);
                }
            }
            base.Expand();
        }

        // A focusable node reads its own label; empty-label structural nodes don't focus.
        public override bool CanFocus => !string.IsNullOrWhiteSpace(_label);

        // The tree supplies level/position; the node contributes label, role, annotation, and (for
        // expandable nodes) the expanded/collapsed state.
        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(_label)) yield return new LabelAnnouncement(Message.Raw(_label));
            if (!string.IsNullOrEmpty(Role)) yield return new RoleAnnouncement(Role);
            if (!string.IsNullOrEmpty(Annotation)) yield return new ValueAnnouncement(Message.Raw(Annotation));
            if (Expandable) yield return new RoleAnnouncement(Expanded ? "expanded" : "collapsed");
        }
    }
}
