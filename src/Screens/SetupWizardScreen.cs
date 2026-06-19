using System;
using System.Collections.Generic;
using WrathAccess.Settings;
using WrathAccess.Speech;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// First-run setup wizard (and re-runnable later): walks a new player through the few high-impact,
    /// preference-driven choices in plain language, reusing the REAL settings controls so the menu stays
    /// the single source of truth. Built on the shared <see cref="WizardScreen"/> shell (Back/Next, focus
    /// re-homing, page-turn on advance). Mod-pushed like the Ctrl+M menu (static open flag); the phase set
    /// is dynamic — step 2 (the chosen engine's own settings) drops out for a paramless engine.
    ///
    /// Step 1: pick the speech engine — selecting one writes it (like normal) and plays a sample so you
    /// hear it working. Step 2: that engine's settings tree, for a clear starting configuration.
    ///
    /// Opened via <see cref="Open"/> (a temporary binding for now; first-launch auto-run + a menu entry
    /// to follow).
    /// </summary>
    public sealed class SetupWizardScreen : WizardScreen
    {
        private enum Step { Backend, HandlerSettings, Navigation }

        private static bool s_open;
        private static Step s_step;
        private static object s_phase = new object();      // identity changes per navigation → shell rebuilds
        private static readonly object Session = new object(); // stable while open → IsActive + no spurious rebuild

        public override string Key => "ctx.setupwizard";
        public override string ScreenName => Loc.T("screen.setup_wizard");
        public override int Layer => 36;       // above the mod menu (35); a modal over the main menu
        public override bool Exclusive => true; // owns the keyboard while open

        public static void Open() { s_open = true; s_step = Step.Backend; s_phase = new object(); }
        private static void Close() { s_open = false; }

        protected override object WizardVm() => s_open ? Session : null;
        protected override object CurrentPhase() => s_phase;

        protected override string PhaseLabel() => TitleFor(s_step);

        private static string TitleFor(Step step)
        {
            switch (step)
            {
                case Step.Backend: return Loc.T("wizard.speech.backend_title");
                case Step.HandlerSettings: return Loc.T("wizard.speech.settings_title", new { name = SelectedHandlerLabel() });
                case Step.Navigation: return Loc.T("wizard.nav.title");
                default: return "";
            }
        }

        // The roadmap header: one jump-target per active phase (like chargen's), each with a LIVE one-line
        // summary of its current choice so you can see what to revisit and jump straight back to it.
        protected override void BuildHeader(Container root)
        {
            var list = new ListContainer(Loc.T("wizard.steps"));
            foreach (var step in ActiveSteps())
            {
                var s = step; // capture for the live closures
                list.Add(new ProxyTab(() => RoadmapLabel(s), () => s_step == s, () => JumpTo(s)));
            }
            root.Add(list);
        }

        private static string RoadmapLabel(Step step)
        {
            var title = TitleFor(step);
            var summary = SummaryFor(step);
            return string.IsNullOrEmpty(summary) ? title : title + ": " + summary;
        }

        private static string SummaryFor(Step step)
        {
            switch (step)
            {
                case Step.Backend: return SelectedHandlerLabel();
                case Step.Navigation:
                    var m = PrimaryMode();
                    return m == "continuous" ? Loc.T("wizard.nav.continuous")
                         : m == "tiled" ? Loc.T("wizard.nav.tiled") : "";
                default: return "";
            }
        }

        // Jump straight to a phase from the roadmap — any active phase, forward or back (the wizard's phases
        // are independent settings, no gating); this is the "go back and adjust later" path.
        private static void JumpTo(Step step)
        {
            if (s_step == step) return;
            s_step = step;
            s_phase = new object();
        }

        protected override void BuildContent(Container content)
        {
            switch (s_step)
            {
                case Step.Backend: BuildBackendStep(content); break;
                case Step.HandlerSettings: BuildHandlerSettingsStep(content); break;
                case Step.Navigation: BuildNavigationStep(content); break;
            }
        }

        // ---- Step 1: choose the speech engine (select + sample) ----

        private static void BuildBackendStep(Container content)
        {
            content.Add(new TextElement(() => Loc.T("wizard.speech.backend_help")));

            var list = new ListContainer();
            foreach (var h in SpeechManager.Handlers)
            {
                if (!CanUse(h)) continue; // only engines that actually load on this machine
                var handler = h;          // capture for the live closures
                list.Add(new ProxyRadioOption(
                    () => handler.Label,
                    () => HandlerChoice()?.ValueId == handler.Key,
                    () => SelectBackend(handler.Key)));
            }
            content.Add(list);
        }

        private static void SelectBackend(string key)
        {
            HandlerChoice()?.Set(key); // selects "like normal" — writes the default config's handler
            var h = SpeechManager.ResolveHandler(key);
            // The sample IS the feedback: interrupt purges the generic handler-changed confirm (and any
            // backlog from rapid switching), so you hear THIS engine demonstrate itself right where you
            // chose it. (Clipboard has no audio — its "sample" lands on the clipboard, which is its nature.)
            Tts.Speak(Loc.T("wizard.speech.sample", new { name = h?.Label ?? key }), interrupt: true);
        }

        // ---- Step 2: that engine's own settings (the real controls) ----

        private static void BuildHandlerSettingsStep(Container content)
        {
            var sub = SelectedHandlerParams();
            if (sub == null || sub.Children.Count == 0)
            {
                content.Add(new TextElement(() => Loc.T("wizard.step_unavailable")));
                return;
            }
            foreach (var child in sub.Children) ModSettingsScreen.BuildSettingNode(content, child);
        }

        // ---- Step 3: exploration movement (one choice → several cursor settings) ----

        private static void BuildNavigationStep(Container content)
        {
            content.Add(new TextElement(() => Loc.T("wizard.nav.help")));

            var list = new ListContainer();
            list.Add(new ProxyRadioOption(() => Loc.T("wizard.nav.continuous"),
                () => PrimaryMode() == "continuous", ApplyContinuous));
            list.Add(new ProxyRadioOption(() => Loc.T("wizard.nav.tiled"),
                () => PrimaryMode() == "tiled", ApplyTiled));
            content.Add(list);
        }

        // Continuous: both cursors glide — primary 15 ft/s, secondary 30 ft/s.
        private static void ApplyContinuous() => ModSettings.Batch(() =>
        {
            SetMode("primary", "continuous"); SetSpeed("primary", 15);
            SetMode("secondary", "continuous"); SetSpeed("secondary", 30);
        });

        // Tiled: primary steps a grid (speed unused); secondary glides at 15 ft/s.
        private static void ApplyTiled() => ModSettings.Batch(() =>
        {
            SetMode("primary", "tiled");
            SetMode("secondary", "continuous"); SetSpeed("secondary", 15);
        });

        // The Default overlay's cursor slots (defaults.cursor.<slot>, see OverlaySettingsRegistry): a mode
        // write re-resolves the Default overlay live and speed is read live, so this applies immediately and
        // persists. We touch only the Default overlay — any custom overlays are left alone.
        private static string PrimaryMode()
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor.primary.mode")?.ValueId;

        private static void SetMode(string slot, string mode)
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor." + slot + ".mode")?.Set(mode);

        private static void SetSpeed(string slot, int feet)
            => ModSettings.GetSetting<IntSetting>("defaults.cursor." + slot + ".speed")?.Set(feet);

        // ---- navigation ----

        // The active step sequence (dynamic — the engine-settings step drops out for a paramless engine,
        // and future phases just append here).
        private static Step[] ActiveSteps()
        {
            var steps = new List<Step> { Step.Backend };
            if (HasHandlerSettings()) steps.Add(Step.HandlerSettings);
            steps.Add(Step.Navigation);
            return steps.ToArray();
        }

        protected override void OnBack() => GoTo(-1);
        protected override void OnNext() => GoTo(+1);

        // Step by ±1 through the active sequence; stepping off either end leaves the wizard.
        private static void GoTo(int delta)
        {
            var steps = ActiveSteps();
            int i = Array.IndexOf(steps, s_step) + delta;
            if (i < 0 || i >= steps.Length) { Close(); return; }
            s_step = steps[i];
            s_phase = new object();
        }

        protected override string NextLabel() => IsLastStep() ? Loc.T("wizard.finish") : Loc.T("wizard.next");

        private static bool IsLastStep()
        {
            var steps = ActiveSteps();
            return Array.IndexOf(steps, s_step) == steps.Length - 1;
        }

        // Escape (ui.back) closes the wizard, like the mod menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        // ---- helpers ----

        private static ChoiceSetting HandlerChoice() => SpeechManager.Default?.Tree?.Get<ChoiceSetting>("handler");

        private static bool CanUse(ISpeechHandler h)
        {
            try { return h.Detect(); } catch { return false; }
        }

        // The concrete engine the default config resolves to (resolves "auto" to a real handler).
        private static string SelectedHandlerLabel()
            => SpeechManager.ResolveHandler(SpeechManager.Default?.HandlerKey)?.Label
               ?? HandlerChoice()?.Current?.Label ?? "";

        private static CategorySetting SelectedHandlerParams()
        {
            var h = SpeechManager.ResolveHandler(SpeechManager.Default?.HandlerKey);
            return h != null ? SpeechManager.Default?.Tree?.Get<CategorySetting>(h.Key) : null;
        }

        private static bool HasHandlerSettings()
        {
            var sub = SelectedHandlerParams();
            return sub != null && sub.Children.Count > 0;
        }
    }
}
