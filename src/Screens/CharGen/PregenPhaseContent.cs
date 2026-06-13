using System.Linq;
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
            {
                var it = item; // capture for the live closures
                // Label = name, race, class, role (skipping blanks). Details (class description +
                // features) are the selected character's, shown by the phase's InfoVM as a drill-in.
                list.Add(new ProxySelectionItem(it,
                    () => string.Join(", ", new[] { it.CharacterName.Value, it.Race.Value, it.Class.Value, it.Role.Value }
                        .Where(p => !string.IsNullOrWhiteSpace(p))),
                    tooltip: () => it.IsSelected.Value && Phase.InfoVM != null ? Phase.InfoVM.CurrentTooltip : null));
            }
            list.Add(new ProxyCustomCharacter(Phase)); // last entry, in the list itself
            content.Add(list);

            _detailPanel = new Panel(Loc.T("chargen.details"));
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

            // One flow-sheet document for the whole build (title sections → regions you arrow through
            // and Ctrl+Up/Down between); glossary links follow on Space. Skip when there's no content.
            var sheet = TooltipFlowBuilder.Build(tpl, includeEmptyNotice: false);
            if (sheet.RowCount == 0) return;
            _detailPanel.Add(sheet);
        }
    }
}
