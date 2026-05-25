using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// On-demand reader for a "complex" (brick) tooltip — opened with Space on a focused element
    /// that provides a TooltipBaseTemplate. Lists the rendered bricks as a vertical list you
    /// arrow through; Escape closes and returns to where you were. Layer 40 so it can sit over
    /// any screen (a tooltip can be requested from anywhere). Mod-pushed (IsActive reads our state).
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
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => CloseTooltip());
        }

        private void Build()
        {
            Clear();
            _builtFor = s_template;
            if (s_template == null) return;

            var list = new ListContainer();
            foreach (var brick in TooltipReader.Build(s_template))
                list.Add(brick);
            if (list.Children.Count == 0)
                list.Add(new TextElement("No tooltip information."));
            Add(list);
        }
    }
}
