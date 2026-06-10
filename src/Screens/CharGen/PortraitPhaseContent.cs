using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Portrait phase: a Default/Custom tab selector + the portraits in the current tab. Portraits
    /// have no names (they're images), so we label them by position; selecting one applies it. The
    /// portrait list rebuilds when you switch tabs. The custom-portrait creator (file import) and
    /// the "create new" item are skipped for now.
    /// </summary>
    public sealed class PortraitPhaseContent : CharGenPhaseContent<CharGenPortraitPhaseVM>
    {
        private Panel _portraitPanel;
        private object _tabFrom;

        public PortraitPhaseContent(CharGenPortraitPhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            var tabs = new ListContainer(Loc.T("label.tabs"));
            foreach (var tab in Phase.TabSelector.EntitiesCollection)
            {
                var t = tab; // capture for the live closure
                tabs.Add(new ProxySelectionItem(t, () => t.Tab.ToString(), role: "tab"));
            }
            content.Add(tabs);

            _portraitPanel = new Panel(Loc.T("chargen.portraits"));
            content.Add(_portraitPanel);
            FillPortraits();
        }

        public override void Tick()
        {
            if (_portraitPanel != null && !ReferenceEquals(Phase.CurrentTab.Value, _tabFrom))
                FillPortraits();
        }

        private void FillPortraits()
        {
            if (_portraitPanel == null) return;
            _portraitPanel.Clear();
            _tabFrom = Phase.CurrentTab.Value;

            var list = new ListContainer();
            int index = 1;
            foreach (var portrait in CurrentPortraits())
            {
                if (portrait == null || portrait.CustomPortraitCreatorItem) continue; // skip "create new"
                var p = portrait; var positional = "Portrait " + index; // capture for the live closure
                // No display name on portraits — use the blueprint's asset name (codey but distinguishing),
                // falling back to the positional label. Portraits play their own selection sound off the VM path.
                list.Add(new ProxySelectionItem(p, () =>
                {
                    var bp = p.GetBlueprintPortrait();
                    var n = bp != null ? bp.name : null;
                    return !string.IsNullOrEmpty(n) ? n : positional;
                }, suppressActivateSound: true));
                index++;
            }
            if (list.Children.Count == 0) list.Add(new TextElement(() => Loc.T("chargen.no_portraits")));
            _portraitPanel.Add(list);
        }

        // Default tab → all built-in portraits (grouped by category); Custom tab → imported ones.
        private IEnumerable<CharGenPortraitSelectorItemVM> CurrentPortraits()
        {
            bool custom = Phase.CurrentTab.Value != null && Phase.CurrentTab.Value.Tab == CharGenPortraitTab.Custom;
            if (custom) return Phase.CustomPortraitGroup.PortraitCollection;
            return Phase.PortraitGroupVms.Values.SelectMany(g => g.PortraitCollection);
        }
    }
}
