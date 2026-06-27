using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.InfoWindow; // InfoWindowVM
using WrathAccess.UI;
using WrathAccess.UI.Tooltips; // TooltipFlowBuilder

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's persistent Info window (<see cref="InfoWindowVM"/> on
    /// <c>CommonVM.TooltipContextVM</c>) — opened by an item's "Information"/"Details" context action
    /// (<c>ItemSlotVM.ShowInfo</c> → <c>TooltipHelper.ShowInfo</c> → <c>HandleInfoRequest</c>) and by
    /// glossary-link info. It's a real, modal window (the game itself gates input on it), unlike the
    /// transient hover tooltip — so without a screen for it a blind player who opened "Details" was trapped
    /// with no way to read or close it.
    ///
    /// We render the window's own templates with the same <see cref="TooltipFlowBuilder"/> the rest of the
    /// mod uses (so headings, glossary <c>&lt;link&gt;</c> drill-in, etc. all work), and Back calls
    /// <see cref="InfoWindowVM.OnClose"/> — the game's own dispose+refocus callback, which nulls the reactive
    /// so we pop and focus returns to the item. Covers both <c>InfoWindowVM</c> (item Details) and
    /// <c>GlossaryInfoWindowVM</c> (same type); unit inspect (<c>InspectWindowVM</c>) is a separate type,
    /// left as a follow-up. Layer 30 (a top-level modal, above the service windows / vendor it opens over).
    /// </summary>
    public sealed class InfoWindowScreen : Screen
    {
        public InfoWindowScreen() { Wrap = true; }

        public override string Key => "overlay.infowindow";
        public override string ScreenName => Loc.T("screen.info");
        public override int Layer => 30;
        public override bool Exclusive => true; // a focused reader — own the keyboard while it's up

        public override bool IsActive() => Vm() != null;

        // Item Details and glossary info share the InfoWindowVM type; whichever is open is the one we read.
        private static InfoWindowVM Vm()
        {
            var t = Game.Instance?.RootUiContext?.CommonVM?.TooltipContextVM;
            if (t == null) return null;
            return t.InfoWindowVM.Value ?? t.GlossaryInfoWindowVM.Value;
        }

        private InfoWindowVM _builtFrom;

        public override void OnPush() { _builtFrom = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm != null && vm != _builtFrom)
            {
                // Window VM swapped (e.g. drilled glossary→info) — re-home focus.
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back/Escape runs the game's own close callback (disposes the window + restores focus); the
            // reactive then nulls, IsActive goes false, and we pop.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.OnClose());
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            // Each template the window carries becomes a FlowSheet (usually one — the item's own template,
            // the same content Space shows). Reuses our whole tooltip pipeline incl. glossary drill-in.
            int added = 0;
            var templates = vm.GetTooltipTemplates();
            if (templates != null)
                foreach (var t in templates)
                {
                    if (t == null) continue;
                    var sheet = TooltipFlowBuilder.Build(t, includeEmptyNotice: false);
                    if (sheet.RowCount > 0) { Add(sheet); added++; }
                }
            if (added == 0) Add(new TextElement(() => Loc.T("tooltip.empty")));
        }
    }
}
