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
        /// content becomes this node's (lazy) children when expanded. The drill-in is a FACTORY,
        /// resolved live when the node is expanded — never a cached template.</summary>
        public static TooltipNode Leaf(string label, string role = null, string annotation = null,
            Func<TooltipBaseTemplate> drillIn = null)
        {
            Func<IEnumerable<TooltipNode>> factory = drillIn == null
                ? null
                : (Func<IEnumerable<TooltipNode>>)(() =>
                {
                    var t = drillIn();
                    return t != null ? TooltipTreeBuilder.Build(t) : Enumerable.Empty<TooltipNode>();
                });
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

        /// <summary>Can be expanded — eager children present, or a lazy drill-in that, once built, has
        /// content. A lazy drill-in is <b>probed</b> here (built once, cached): many feature/source
        /// tooltips are header-only, so we must build to know whether there's anything beyond the
        /// redundant header we drop — otherwise the node would falsely announce "collapsed" and then
        /// give "No details" on Right. Probing is driven by focus: <see cref="GetFocusAnnouncements"/>
        /// evaluates this only after the label/role parts, so focusing a node builds its drill-in, while
        /// <see cref="UIElement.GetLabelText"/> (which stops at the label) and tree construction do not.
        /// A drill-in is also suppressed when an ancestor shares its label (feature tooltips embed the
        /// feature as their header → would re-open forever, e.g. Sneak Attack → Sneak Attack → …).</summary>
        public override bool Expandable
        {
            get
            {
                if (Children.Count > 0) return true;        // eager group / already-built drill-in
                if (_built) return false;                   // probed and hollow (header-only)
                if (_childFactory == null) return false;    // plain leaf
                if (HasAncestorLabel(_label)) return false; // lazy drill-in that would cycle
                BuildChildren();                            // probe once so we report honestly
                return Children.Count > 0;
            }
        }

        private bool HasAncestorLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            for (var p = Parent; p != null; p = p.Parent)
                if (string.Equals(p.Label, label)) return true;
            return false;
        }

        /// <summary>Mark expanded. Children are already present — either eager, or built by the probe in
        /// <see cref="Expandable"/> (which Expand consults first). A hollow/leaf node is a no-op.</summary>
        public override void Expand()
        {
            if (!Expandable) return; // probes the lazy drill-in if needed; false ⇒ nothing to expand into
            base.Expand();
        }

        // Build the lazy drill-in's children once (cached). A drill-in's content is usually fronted by a
        // header repeating this node's label (a glossary title, a feature name); drop that redundant
        // header — but if it's a GROUP, splice in its children (the real content is nested inside);
        // only a bare leaf header is dropped outright.
        private void BuildChildren()
        {
            if (_built || _childFactory == null) return;
            _built = true;
            foreach (var child in _childFactory() ?? Enumerable.Empty<TooltipNode>())
            {
                if (child == null) continue;
                if (string.Equals(child.Label, _label))
                    foreach (var grandchild in child.Children) Add(grandchild);
                else
                    Add(child);
            }
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
