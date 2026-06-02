using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// On-demand reader for a "complex" (brick) tooltip — opened with Space on a focused element
    /// that provides a TooltipBaseTemplate. Renders it as a treeview (groups by title rank; arrow
    /// through it, Right/Left expand/collapse, drill-in nodes expand on demand). Structural groups
    /// start expanded so the tooltip reads fully on open; Escape closes and returns to where you
    /// were. Layer 40 so it can sit over any screen. Mod-pushed (IsActive reads our state).
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private static TooltipBaseTemplate s_template;

        public static void Open(TooltipBaseTemplate template) => s_template = template;

        public static void CloseTooltip() => s_template = null;

        public override string Key => "overlay.tooltip";
        // No ScreenName: opening jumps straight to the tooltip content (you asked for it by
        // pressing the key — the point is to read it fast, not hear "… tooltip" first).
        public override int Layer => 40; // above everything — a tooltip can be read over any screen
        public override bool IsActive() => s_template != null;

        private TooltipBaseTemplate _builtFor;

        public override void OnPush() { _builtFor = null; Build(); }
        public override void OnPop() { Clear(); _builtFor = null; }
        public override void OnUpdate() { if (s_template != _builtFor) Build(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseTooltip());
        }

        private void Build()
        {
            Clear();
            _builtFor = s_template;
            if (s_template == null) return;

            var root = new TreeGroup();
            foreach (var node in TooltipTreeBuilder.Build(s_template))
                root.Add(node);
            if (root.Children.Count == 0)
                root.Add(TooltipNode.Leaf("No tooltip information."));
            TooltipTreeBuilder.ExpandStructural(root); // read fully on open; drill-ins stay lazy
            Add(root);
        }
    }
}
