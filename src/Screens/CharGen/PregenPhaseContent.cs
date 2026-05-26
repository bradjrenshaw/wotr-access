using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Pregen phase: the premade-character list (ending with Custom Character) + a live Details
    /// panel — the selected option's build, rendered as a single treeview from its tooltip (one
    /// Tab-stop you arrow through; groups collapse/expand), refreshed on selection.
    /// </summary>
    public sealed class PregenPhaseContent : CharGenPhaseContent<CharGenPregenPhaseVM>
    {
        private Panel _detailPanel;
        private object _detailFrom;

        public PregenPhaseContent(CharGenPregenPhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            var list = new ListContainer();
            foreach (var item in Phase.PregenSelectionGroup.EntitiesCollection)
                list.Add(new ProxyPregenItem(item, Phase));
            list.Add(new ProxyCustomCharacter(Phase)); // last entry, in the list itself
            content.Add(list);

            _detailPanel = new Panel("Details");
            content.Add(_detailPanel);
            FillDetail();
        }

        public override void Tick()
        {
            if (_detailPanel != null && !ReferenceEquals(Phase.SelectedPregenEntity.Value, _detailFrom))
                FillDetail();
        }

        private void FillDetail()
        {
            if (_detailPanel == null) return;
            _detailPanel.Clear();
            _detailFrom = Phase.SelectedPregenEntity.Value;
            var tpl = Phase.InfoVM != null ? Phase.InfoVM.CurrentTooltip : null;
            if (tpl == null) return;

            // One treeview for the whole build (title ranks → groups), not a stack of section
            // tab-stops. Structural groups start expanded so it reads fully; drill-ins stay lazy.
            var tree = new TreeGroup();
            foreach (var node in TooltipTreeBuilder.Build(tpl))
                tree.Add(node);
            if (tree.Children.Count == 0) return;
            TooltipTreeBuilder.ExpandStructural(tree);
            _detailPanel.Add(tree);
        }
    }
}
