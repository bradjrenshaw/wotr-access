using System.Collections.Generic;
using Kingmaker.UI.Common; // UIUtility.GetAlignmentName
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Alignment;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateAlignment
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Alignment phase: the nine alignments as a radio list (Lawful/Neutral/Chaotic × Good/Neutral/Evil,
    /// in the game's row-major order). The names are self-describing, so a flat list reads cleanly
    /// without the visual wheel. Class-restricted alignments read "disabled" and can't be picked (via
    /// IsAvailable); Space on any one opens its description (TooltipTemplateAlignment). The sector list
    /// is built lazily on entering detailed view, so we (re)build when it materializes.
    /// </summary>
    public sealed class AlignmentPhaseContent : CharGenPhaseContent<CharGenAlignmentPhaseVM>
    {
        private Panel _listPanel;
        private int _count = -1;

        public AlignmentPhaseContent(CharGenAlignmentPhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            _listPanel = new Panel();
            content.Add(_listPanel);
            FillList();
        }

        public override void Tick()
        {
            if (Count() != _count) FillList();
        }

        private void FillList()
        {
            if (_listPanel == null) return;
            var sectors = Phase.AlignmentSectorViewModels;
            _count = sectors != null ? sectors.Count : 0;
            _listPanel.Clear();
            if (sectors == null || sectors.Count == 0) return;
            var list = new ListContainer(Loc.T("chargen.alignments"));
            foreach (var sector in sectors)
            {
                if (sector == null) continue;
                var s = sector; // capture for the live closures
                list.Add(new ProxySelectionItem(s,
                    () => UIUtility.GetAlignmentName(s.Alignment),
                    () => new TooltipTemplateAlignment(s.Alignment, isUndetectable: false)));
            }
            _listPanel.Add(list);
        }

        private int Count() => Phase.AlignmentSectorViewModels != null ? Phase.AlignmentSectorViewModels.Count : 0;
    }
}
