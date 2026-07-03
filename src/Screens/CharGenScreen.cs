using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.CharGen;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Character generation / level-up (CharGenVM) on the shared <see cref="WizardScreen"/> shell,
    /// graph-native. This same VM drives initial chargen, every in-game companion/mythic level-up, and
    /// respec — only the host differs, so we detect it in the main-menu OR in-game context. The header
    /// is the roadmap strip: one live entry per phase (name + state + summary, each a jump target),
    /// rendered fresh every frame so the phase SET changing (a class adding Spells, a race adding
    /// phases) just shows up — the old set-polling/rebuild machinery is gone. Next advances the phase
    /// (or Complete on the last), Back retreats (or Close on the first); Next is gated by the current
    /// phase's completion. Phase content is per-phase (<see cref="CharGenPhaseContentFactory"/>).
    /// </summary>
    public sealed class CharGenScreen : WizardScreen
    {
        public override string Key => "ctx.chargen";
        public override int Layer => 15; // full-screen flow: above game contexts + service windows
        // No ScreenName — the content is labeled with the current phase's name.

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

        protected override void BuildContent(GraphBuilder b, string k)
        {
            var content = CharGenPhaseContentFactory.Create(Vm()?.CurrentPhaseVM.Value);
            if (content != null)
                content.Build(b, k);
            else
                b.AddItem(ControlId.Structural(k + "unavailable"),
                    GraphNodes.Text(() => Loc.T("chargen.phase_unavailable")));
        }

        // The roadmap strip (top of screen): one entry per phase, name + state + live summary, each a
        // jump target — read live from PhasesCollection each render, so set changes just render.
        protected override void BuildHeader(GraphBuilder b)
        {
            var phases = Vm()?.PhasesCollection;
            if (phases == null) return;
            b.BeginStop("roadmap").PushContext(Loc.T("chargen.steps"), "list");
            int i = 0;
            foreach (var p in phases)
            {
                if (p == null) { i++; continue; }
                var phase = p; // capture for the live summary closure
                b.AddItem(ControlId.Referenced(phase, "cg:step:" + i),
                    CharGenNodes.RoadmapEntry(phase, () => RoadmapSummary.For(phase)));
                i++;
            }
            b.PopContext();
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
            if (IsLastPhase(vm))
            {
                // The game's view plays this on completion (CharGenView.GoToNextPhaseOrComplete);
                // driving Complete() from the VM bypasses it, so replay it here. Phase advances play
                // the page-turn instead (WizardScreen, on phase change) — completion deliberately doesn't.
                vm.Complete();
                UiSound.Play(UISoundType.ChargenCompleteClick);
            }
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
