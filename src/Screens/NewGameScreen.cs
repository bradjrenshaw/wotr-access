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
    /// The New Game wizard (MainMenuVM.NewGameVM). A linear Next/Back wizard whose pages are
    /// phases (Story → Difficulty → Save injector). Structure mirrors Settings: a content panel
    /// labeled with the current step, plus Back/Next footer buttons (Next's label comes from the
    /// phase's NextStepTitle, e.g. "Start" on the last step). Advancing past the last step enters
    /// character generation; backing past the first exits to the menu. Rebuilds on step change.
    ///
    /// Story is implemented; Difficulty (reuses the Settings entity proxies) and the save injector
    /// are placeholders for now.
    /// </summary>
    public sealed class NewGameScreen : Screen
    {
        public NewGameScreen() { Wrap = true; }

        public override string Key => "ctx.newgame";
        public override string ScreenName => "New Game";
        public override int Layer => 0; // mutually exclusive with the main-menu sidebar

        public override bool IsActive() => Vm() != null;

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

        private NewGameVM _builtFrom;
        private object _lastStep;

        public override void OnPush() { _builtFrom = null; _lastStep = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; _lastStep = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm != _builtFrom)
            {
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
                return;
            }
            var step = vm.MenuSelectionGroup.SelectedEntity.Value;
            if (!ReferenceEquals(step, _lastStep))
            {
                // Phase changed (Next/Back, or a step pick) — rebuild and land on the new page.
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            var step = vm.MenuSelectionGroup.SelectedEntity.Value;
            _lastStep = step;
            var phase = step != null ? step.NewGamePhaseVM : null;

            // Content panel, labeled with the current step so entering it announces the phase.
            var content = new Panel(step != null ? step.Title : null);
            if (phase is NewGamePhaseStoryVM story)
                BuildStory(content, story);
            else if (phase is NewGamePhaseDifficultyVM difficulty)
                SettingsEntityBuilder.BuildInto(content, difficulty.SettingEntities); // same VMs as Settings
            else
                content.Add(new ProxyLabel("This step is not accessible yet."));
            Add(content);

            // Footer: Back, then Next (label + availability track the current phase live).
            Add(new ProxyActionButton("Back", () => true, () => vm.OnButtonBack()));
            Add(new ProxyActionButton(
                () => phase != null && phase.NextStepTitle != null ? phase.NextStepTitle.Value : "Next",
                () => phase == null || phase.IsButtonNextAvailable.Value,
                () => vm.OnButtonNext()));
        }

        private static void BuildStory(Panel content, NewGamePhaseStoryVM story)
        {
            // Campaign choices (unlabeled list → reads as "<name>, option, selected, N of M").
            var campaigns = new ListContainer();
            foreach (var e in story.SelectionGroup.EntitiesCollection)
                campaigns.Add(new ProxyScenario(e));
            content.Add(campaigns);

            // Live description of the currently-selected campaign (updates as you pick).
            content.Add(new ProxyLabel(
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
