using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Pregen phase: the premade-character list (ending with Custom Character) + a live Details
    /// panel (the selected option's build), split into tab-between sections, refreshed on selection.
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

            foreach (var section in TooltipReader.BuildSections(tpl))
            {
                var sectionList = new ListContainer(section.Title);
                foreach (var el in section.Elements) sectionList.Add(el);
                _detailPanel.Add(sectionList);
            }
        }
    }
}
