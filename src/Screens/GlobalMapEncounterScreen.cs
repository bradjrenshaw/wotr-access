using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapRandomEncounterVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world-map travel popup (<see cref="GlobalMapRandomEncounterVM"/>) the game raises when travel
    /// stops for an encounter / discovery — rendered like the dialogue transcript: ONE FlowSheet with the
    /// event line (Title + Description) as the focused cue row, then an answers region of the real choices
    /// (Enter = the game's <c>EnterLabel</c> action, Avoid when offered). Focus lands on the cue silently and
    /// the line is delivered as speech; Down reaches the choices, and when Continue is the only way forward
    /// (Avoid disabled) Enter on the cue advances it — exactly like dialogue. Buttons fire the game's
    /// button-click sound + the VM method (Accept/Avoid), the same as pressing the real control. A modal:
    /// <see cref="Exclusive"/> blocks the world-map keys beneath, and being the top screen (not ctx.globalmap)
    /// drops OverlayManager.Active so the world-map cursor + sonar freeze.
    /// </summary>
    public sealed class GlobalMapEncounterScreen : Screen
    {
        public override string Key => "ctx.globalmapencounter";
        public override string ScreenName => Loc.T("screen.world_map_encounter");
        public override int Layer => 15; // a modal over the world-map base context (like dialogue)
        public override bool Exclusive => true;

        private GlobalMapRandomEncounterVM _builtVm; // the popup the sheet was built for
        private GlobalMapRandomEncounterVM _spokenVm; // the popup we've delivered as speech

        private static GlobalMapRandomEncounterVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapRandomEncounterVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Clear(); _builtVm = null; _spokenVm = null; }
        public override void OnPop() { Clear(); _builtVm = null; _spokenVm = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm != _builtVm) { _builtVm = vm; Rebuild(vm); }
            // Deliver the line once, queued (never interrupting), like a dialogue cue landing on screen.
            if (vm != _spokenVm) { _spokenVm = vm; Tts.Speak(CueText(vm), interrupt: false); }
        }

        private void Rebuild(GlobalMapRandomEncounterVM vm)
        {
            Clear();
            var sheet = new FlowSheet();

            // The event line as the cue row (Title + Description). When Avoid is disabled, Continue is the
            // only way forward, so Enter on the cue advances it (dialogue's Enter-through-exposition).
            var log = sheet.List(null);
            var cue = new EncounterCue(() => CueText(vm), () => vm.AvoidIsDisable, AcceptLive);
            log.Item(cue);

            // The real choices: Enter (Continue/Fight) + Avoid (only when offered).
            var ans = sheet.List(null);
            string enterLabel = TextUtil.StripRichText(vm.EnterLabel);
            ans.Item(new ProxyActionButton(() => enterLabel, () => true, AcceptLive));
            if (!vm.AvoidIsDisable)
            {
                string avoidLabel = TextUtil.StripRichText(vm.AvoidLabel);
                ans.Item(new ProxyActionButton(() => avoidLabel, () => true, AvoidLive));
            }
            sheet.Reflow();

            Add(sheet);
            Navigation.Attach(this);
            Navigation.Focus(cue, announce: false); // land on the cue silently; OnUpdate delivers it as speech
        }

        // Accept / Avoid on the LIVE VM (what the OwlcatButtons are wired to), each with the game's
        // button-click sound — identical to pressing the real button.
        private static void AcceptLive() { PlayClick(); Vm()?.Accept(); }
        private static void AvoidLive() { PlayClick(); Vm()?.Avoid(); }

        private static void PlayClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();

        // Title + Description as one spoken line ("Title. Description"), colours stripped.
        private static string CueText(GlobalMapRandomEncounterVM vm)
        {
            var title = TextUtil.StripRichText(vm.Title);
            var desc = TextUtil.StripRichText(vm.Description);
            if (string.IsNullOrWhiteSpace(title)) return desc ?? string.Empty;
            if (string.IsNullOrWhiteSpace(desc)) return title;
            return title + ". " + desc;
        }

        // Escape opens the game menu, like the rest of the world map (the game's EscManager is muted while
        // focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }

        // The cue line: focusing re-reads it; when Avoid is disabled (Continue is the only way forward),
        // Enter here advances it too — mirroring the dialogue cue row. The button-click sound comes from
        // AcceptLive, so the row itself stays silent on activate.
        private sealed class EncounterCue : TextElement
        {
            private readonly Func<bool> _single;
            private readonly Action _accept;

            public EncounterCue(Func<string> text, Func<bool> single, Action accept) : base(text)
            { _single = single; _accept = accept; }

            public override Kingmaker.UI.UISoundType? ActivateSound => null;

            public override IEnumerable<ElementAction> GetActions()
            {
                if (_single())
                    yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.choose"), _ => _accept());
            }
        }
    }
}
