using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.InfoWindow; // InfoWindowVM
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateItem (comparisons)
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate, TooltipTemplateType
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's persistent Info window (<see cref="InfoWindowVM"/> on <c>CommonVM.TooltipContextVM</c>) —
    /// opened by an item's "Information"/"Details" context action (<c>ItemSlotVM.ShowInfo</c> →
    /// <c>TooltipHelper.ShowInfo</c> → <c>HandleInfoRequest</c>) and by glossary-link info. It's a real,
    /// modal window (the game itself gates input on it), unlike the transient hover tooltip — so without a
    /// screen for it a blind player who opened "Details" was trapped with no way to read or close it.
    /// Rendering + close lifecycle live in <see cref="TemplateWindowScreen"/>; here we just point at the
    /// reactive (item Details and glossary share the <see cref="InfoWindowVM"/> type) and its OnClose.
    /// </summary>
    public sealed class InfoWindowScreen : TemplateWindowScreen
    {
        public override string Key => "overlay.infowindow";
        public override string ScreenName => Loc.T("screen.info");

        private static InfoWindowVM Vm()
        {
            var t = Game.Instance?.RootUiContext?.CommonVM?.TooltipContextVM;
            if (t == null) return null;
            return t.InfoWindowVM.Value ?? t.GlossaryInfoWindowVM.Value;
        }

        protected override object Window => Vm();
        protected override IEnumerable<TooltipBaseTemplate> Templates() => Vm()?.GetTooltipTemplates();
        protected override void CloseWindow() => Vm()?.OnClose();

        // ---- item comparisons ----
        //
        // The sighted hover shows an item's tooltip WITH the equipped item(s) it would replace beside
        // it (the slot VM's leading comparative templates). The Info window holds only the item, so
        // the opener hands us the comparisons just before ShowInfo; they bind to the NEXT Info window
        // that appears and render as further TAB-STOPS after the item's own rows — Tab lands on
        // "Equipped: <name>" and arrows read that item's tooltip in place, Shift+Tab returns to the
        // item. The comparison templates are prepared as plain Tooltips, not Info, so reading one
        // never runs that item's open-description hooks.
        private static List<TooltipBaseTemplate> _pending;
        private static List<TooltipBaseTemplate> _comparisons;
        private static object _comparisonsWindow;

        /// <summary>Attach comparison templates to the next Info window opened (null = none).</summary>
        public static void OfferComparisons(List<TooltipBaseTemplate> comparisons)
        {
            _pending = comparisons != null && comparisons.Count > 0 ? comparisons : null;
        }

        private static List<TooltipBaseTemplate> ComparisonsFor(object window)
        {
            if (window == null) return null;
            if (!ReferenceEquals(window, _comparisonsWindow))
            {
                // A new window: adopt whatever was offered for it (a glossary/other window that
                // opened without an offer gets none), then clear the offer.
                _comparisonsWindow = window;
                _comparisons = _pending;
                _pending = null;
            }
            return _comparisons;
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window;
            var comparisons = ComparisonsFor(w);
            if (comparisons != null) b.BeginStop("item"); // the item's own rows become the first stop
            base.Build(b);
            if (comparisons == null) return;
            string k = "tplwin:" + w.GetHashCode() + ":cmp:";
            for (int i = 0; i < comparisons.Count; i++)
            {
                var tpl = comparisons[i];
                if (tpl == null) continue;
                var name = (tpl as TooltipTemplateItem)?.m_Item?.Name ?? "";
                b.BeginStop(k + i).PushContext(Loc.T("item.compared_equipped", new { name }), "list");
                int rows = WrathAccess.UI.Tooltips.TooltipFlowBuilder.Emit(b, k + i + ":", tpl,
                    TooltipTemplateType.Tooltip, includeEmptyNotice: false);
                if (rows == 0)
                    b.AddItem(ControlId.Structural(k + i + ":empty"), GraphNodes.Text(() => Loc.T("tooltip.empty")));
                b.PopContext();
            }
        }
    }
}
