using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Owlcat.Runtime.UI.Tooltips; // TooltipTemplateType
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Race phase: the race list, a gender selector, and a live Details panel. Race has no nested
    /// sub-selection (unlike class archetypes) and no Short/Mechanic toggle — the detail is just the
    /// template the game feeds its InfoSection (<c>ReactiveTooltipTemplate</c> = TooltipTemplateLevelUpRace:
    /// ability-score bonuses, racial features, description), rendered as one treeview at
    /// <see cref="TooltipTemplateType.Info"/>, exactly like the class phase's Short mode. Selecting goes
    /// through the game's own SetSelectedFromView; the detail refreshes on race or gender change.
    /// </summary>
    public sealed class RacePhaseContent : CharGenPhaseContent<CharGenRacePhaseVM>
    {
        private Panel _detailPanel;
        private object _raceFrom;
        private object _genderFrom;

        public RacePhaseContent(CharGenRacePhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            var raceList = new ListContainer(Loc.T("chargen.races"));
            if (Phase.RaceSelector?.EntitiesCollection != null)
                foreach (var item in Phase.RaceSelector.EntitiesCollection)
                    if (item != null) raceList.Add(new ProxySelectionItem(item, () => item.DisplayName));
            content.Add(raceList);

            var genderList = new ListContainer(Loc.T("chargen.gender"));
            if (Phase.GenderSelector?.EntitiesCollection != null)
                foreach (var g in Phase.GenderSelector.EntitiesCollection)
                    if (g != null) genderList.Add(new ProxySelectionItem(g, () => g.DisplayName));
            content.Add(genderList);

            _detailPanel = new Panel(Loc.T("chargen.details"));
            content.Add(_detailPanel);

            _raceFrom = Phase.SelectedRaceVM.Value;
            _genderFrom = Phase.SelectedGenderVM.Value;
            FillDetail();
        }

        public override void Tick()
        {
            var race = Phase.SelectedRaceVM.Value;
            var gender = Phase.SelectedGenderVM.Value;
            if (!ReferenceEquals(race, _raceFrom) || !ReferenceEquals(gender, _genderFrom))
            {
                _raceFrom = race;
                _genderFrom = gender;
                FillDetail();
            }
        }

        private void FillDetail()
        {
            if (_detailPanel == null) return;
            _detailPanel.Clear();
            var tpl = Phase.ReactiveTooltipTemplate.Value;
            if (tpl == null) return;
            var tree = new TreeGroup();
            foreach (var node in TooltipTreeBuilder.Build(tpl, TooltipTemplateType.Info))
                tree.Add(node);
            if (tree.Children.Count == 0) return;
            TooltipTreeBuilder.ExpandStructural(tree); // read fully on focus; drill-ins stay lazy
            _detailPanel.Add(tree);
        }
    }
}
