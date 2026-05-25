using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.CharGen;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Character generation / level-up (CharGenVM) on the shared <see cref="WizardScreen"/> shell.
    /// This same VM drives initial chargen, every in-game companion/mythic level-up, and respec —
    /// only the host differs, so we detect it in the main-menu OR in-game context. Next advances
    /// the phase (or Complete on the last), Back retreats (or Close on the first); Next is gated by
    /// the current phase's completion. Phase content is filled in per-phase (M1+); for now each
    /// phase shows a placeholder.
    /// </summary>
    public sealed class CharGenScreen : WizardScreen
    {
        public override string Key => "ctx.chargen";
        public override int Layer => 15; // full-screen flow: above game contexts + service windows
        // No ScreenName — the content panel is labeled with the current phase's name.

        private static CharGenVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            // Same VM whether reached from the main menu (new game) or in-game (level-up/respec).
            var menu = rc.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
            if (menu != null) return menu;
            return rc.InGameVM?.StaticPartVM?.CharGenContextVM?.CharGenVM?.Value;
        }

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => Vm()?.CurrentPhaseVM.Value;
        protected override string PhaseLabel() => Vm()?.CurrentPhaseVM.Value?.PhaseName.Value;

        // The phase's selection-driven detail panel (rendered inline from InfoVM.CurrentTooltip),
        // plus the selection it was last built for — refreshed in place when selection changes.
        private Panel _detailPanel;
        private object _detailFrom;

        protected override void BuildContent(Container content)
        {
            _detailPanel = null;
            _detailFrom = null;
            var phase = Vm()?.CurrentPhaseVM.Value;
            if (phase is CharGenPregenPhaseVM pregen)
                BuildPregen(content, pregen);
            else
                content.Add(new TextElement("This phase is not accessible yet."));
        }

        // Pregen phase: a list of premade characters ending with Custom Character, then a live
        // Details panel (the selected option's build), split into sections you tab between.
        private void BuildPregen(Container content, CharGenPregenPhaseVM phase)
        {
            var list = new ListContainer();
            foreach (var item in phase.PregenSelectionGroup.EntitiesCollection)
                list.Add(new ProxyPregenItem(item, phase));
            list.Add(new ProxyCustomCharacter(phase)); // last entry, in the list itself
            content.Add(list);

            _detailPanel = new Panel("Details");
            content.Add(_detailPanel);
            FillDetail(phase);
        }

        // Render the selected option's detail template (InfoVM) into the Details panel as a set of
        // tab-between section lists (Overview / Features and Abilities / Ability Scores / Skills).
        private void FillDetail(CharGenPregenPhaseVM phase)
        {
            if (_detailPanel == null) return;
            _detailPanel.Clear();
            _detailFrom = phase.SelectedPregenEntity.Value;
            var tpl = phase.InfoVM != null ? phase.InfoVM.CurrentTooltip : null;
            if (tpl == null) return;

            foreach (var section in TooltipReader.BuildSections(tpl))
            {
                var sectionList = new ListContainer(section.Title);
                foreach (var el in section.Elements) sectionList.Add(el);
                _detailPanel.Add(sectionList);
            }
        }

        protected override void OnPhaseTick()
        {
            // Refresh the Details panel in place when the selection changes (focus stays on the
            // list — selection only changes from there, so this never disturbs the focus path).
            if (_detailPanel != null
                && Vm()?.CurrentPhaseVM.Value is CharGenPregenPhaseVM pregen
                && !ReferenceEquals(pregen.SelectedPregenEntity.Value, _detailFrom))
            {
                FillDetail(pregen);
            }
        }

        protected override void OnBack()
        {
            var vm = Vm();
            if (vm == null) return;
            // Mirrors the game's view: first phase → close chargen; otherwise step back a phase.
            if (IsFirstPhase(vm)) vm.Close();
            else vm.PhasesSelectionGroupRadioVM.SelectPrevValidEntity();
        }

        protected override void OnNext()
        {
            var vm = Vm();
            if (vm == null) return;
            if (IsLastPhase(vm)) vm.Complete();
            else vm.PhasesSelectionGroupRadioVM.SelectNextValidEntity();
        }

        // The view labels the button "Complete" only on the last phase (== PhasesCollection.Last()),
        // else "Next" — note LastPhase is the *previously-shown* phase, not the final one.
        protected override string NextLabel() =>
            IsLastPhase(Vm()) ? (string)UIStrings.Instance.CharGen.Complete : (string)UIStrings.Instance.CharGen.Next;

        protected override bool NextEnabled() => Vm()?.CurrentPhaseIsCompleted.Value ?? false;

        private static bool IsLastPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.LastOrDefault());

        private static bool IsFirstPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.FirstOrDefault());
    }
}
