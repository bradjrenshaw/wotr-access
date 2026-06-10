using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.NewGame;
using Kingmaker.UI.MVVM._VM.NewGame.Difficulty;
using Kingmaker.UI.MVVM._VM.NewGame.Story;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The New Game wizard (MainMenuVM.NewGameVM): Story → Difficulty → Save injector, on the
    /// shared <see cref="WizardScreen"/> shell. Next/Back delegate to the VM's OnButton*; the Next
    /// label/availability come from the current phase. Advancing past the last step enters
    /// character generation; backing past the first exits to the menu. Story + Difficulty are
    /// implemented; the save injector is a placeholder.
    /// </summary>
    public sealed class NewGameScreen : WizardScreen
    {
        public override string Key => "ctx.newgame";
        public override string ScreenName => Loc.T("screen.new_game");
        public override int Layer => 0; // mutually exclusive with the main-menu sidebar

        private static NewGameVM Vm()
        {
            var g = Game.Instance;
            var mm = g != null && g.RootUiContext != null ? g.RootUiContext.MainMenuVM : null;
            if (mm == null || mm.NewGameVM == null) return null;
            // Once character generation opens, the wizard is done — hand off.
            if (mm.CharGenContextVM != null && mm.CharGenContextVM.CharGenVM != null
                && mm.CharGenContextVM.CharGenVM.Value != null) return null;
            return mm.NewGameVM;
        }

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => Vm()?.MenuSelectionGroup.SelectedEntity.Value;

        protected override string PhaseLabel() => Vm()?.MenuSelectionGroup.SelectedEntity.Value?.Title;

        protected override void BuildContent(Container content)
        {
            var phase = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            if (phase is NewGamePhaseStoryVM story)
                BuildStory(content, story);
            else if (phase is NewGamePhaseDifficultyVM difficulty)
                // Same VMs as the Settings screen — build them as a treeview (collapsible header groups,
                // one Tab-stop each, arrow within) to match it, rather than a flat wall of Tab-stops.
                SettingsEntityBuilder.BuildInto(content, difficulty.SettingEntities, tree: true);
            else
                content.Add(new TextElement(() => Loc.T("wizard.step_unavailable")));
        }

        protected override void OnBack() => Vm()?.OnButtonBack();
        protected override void OnNext() => Vm()?.OnButtonNext();

        protected override string NextLabel()
        {
            var p = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            return p != null && p.NextStepTitle != null ? p.NextStepTitle.Value : Loc.T("wizard.next");
        }

        protected override bool NextEnabled()
        {
            var p = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            return p == null || p.IsButtonNextAvailable.Value;
        }

        private static void BuildStory(Container content, NewGamePhaseStoryVM story)
        {
            // Campaign choices (unlabeled list → reads as "<name>, radio button, selected, N of M").
            var campaigns = new ListContainer();
            foreach (var e in story.SelectionGroup.EntitiesCollection)
            {
                var ent = e; // capture for the live closure
                campaigns.Add(new ProxySelectionItem(ent, () => ent.Title));
            }
            content.Add(campaigns);

            // Live description of the currently-selected campaign (updates as you pick).
            content.Add(new TextElement(
                () => story.Description != null ? story.Description.Value : "", "description"));

            // Hardcore/permadeath mode toggle (code name "Last Azlanti"; the localized label
            // reads "Sink or Swim Mode"). Only enabled for dungeon campaigns (Midnight Isles),
            // and the game hides it otherwise — so hideWhenDisabled drops it from nav there too.
            content.Add(new ProxyBoolToggle(
                (string)UIStrings.Instance.NewGameWin.LastAzlantiMode,
                () => story.LastAzlantiIsOn.Value,
                () => story.SwitchLastAzlanti(),
                () => story.LastAzlantiEnabled.Value,
                hideWhenDisabled: true));
        }
    }
}
