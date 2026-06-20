using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI; // UISoundType
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
        private enum Step { Backend, HandlerSettings, Navigation, EventFeedback, EnemyVoice, AllyVoice }

        private static bool s_open;
        private static Step s_step;
        private static object s_phase = new object();      // identity changes per navigation → shell rebuilds
        private static readonly object Session = new object(); // stable while open → IsActive + no spurious rebuild

        public override string Key => "ctx.setupwizard";
        public override string ScreenName => Loc.T("screen.setup_wizard");
        public override int Layer => 36;       // above the mod menu (35); a modal over the main menu
        public override bool Exclusive => true; // owns the keyboard while open

        public static void Open() { s_open = true; s_step = Step.Backend; s_phase = new object(); }
        private static void Close()
        {
            s_open = false;
            // First-run: remember the wizard's been shown so it won't auto-launch on future boots. Any
            // dismissal (Finish, backing off the first step, or Escape) counts; re-run from the Ctrl+M menu.
            ModSettings.GetSetting<BoolSetting>("wizard.completed")?.Set(true);
        }

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
                case Step.EventFeedback: return Loc.T("wizard.events.title");
                case Step.EnemyVoice: return Loc.T("wizard.events.enemy_voice_title");
                case Step.AllyVoice: return Loc.T("wizard.events.ally_voice_title");
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
                case Step.EventFeedback: return Loc.T("wizard.events." + CurrentMode());
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
                case Step.EventFeedback: BuildEventFeedbackStep(content); break;
                case Step.EnemyVoice: BuildEnemyVoiceStep(content); break;
                case Step.AllyVoice: BuildAllyVoiceStep(content); break;
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
            content.Add(SettingsTree(sub.Children));
        }

        // Build settings the way the menu does: into an UNLABELED structural TreeGroup root, so the controls
        // are ONE Tab-stop navigated by up/down arrows (not a Tab per control).
        private static TreeGroup SettingsTree(IEnumerable<Setting> settings)
        {
            var tree = new TreeGroup();
            foreach (var s in settings) ModSettingsScreen.BuildSettingNode(tree, s);
            return tree;
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

        // Continuous: both cursors glide. In-area — primary 15 ft/s, secondary 30 ft/s. World map — both
        // glide too, primary 8 mi/s, secondary 45 mi/s.
        private static void ApplyContinuous() => ModSettings.Batch(() =>
        {
            SetMode("primary", "continuous"); SetSpeed("primary", 15);
            SetMode("secondary", "continuous"); SetSpeed("secondary", 30);
            SetWorldMapMode("primary", "continuous"); SetWorldMapSpeed("primary", 8);
            SetWorldMapMode("secondary", "continuous"); SetWorldMapSpeed("secondary", 45);
        });

        // Tiled: primary steps a grid (speed unused), secondary glides. In-area — secondary 15 ft/s. World
        // map — primary steps a 2-mile grid, secondary glides at 8 mi/s.
        private static void ApplyTiled() => ModSettings.Batch(() =>
        {
            SetMode("primary", "tiled");
            SetMode("secondary", "continuous"); SetSpeed("secondary", 15);
            SetWorldMapMode("primary", "tiled"); SetWorldMapTileSize(2);
            SetWorldMapMode("secondary", "continuous"); SetWorldMapSpeed("secondary", 8);
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

        // The same Default-overlay cursor slots also carry the SEPARATE world-map cursor settings (read live
        // by GlobalMapCursor): movement type + glide speed in miles/sec; the tiled tile size lives on the
        // grid system. Writes apply immediately and persist, just like the in-area pair above.
        private static void SetWorldMapMode(string slot, string mode)
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor." + slot + ".worldmap_mode")?.Set(mode);

        private static void SetWorldMapSpeed(string slot, int miles)
            => ModSettings.GetSetting<IntSetting>("defaults.cursor." + slot + ".worldmap_speed")?.Set(miles);

        private static void SetWorldMapTileSize(int miles)
            => ModSettings.GetSetting<IntSetting>("defaults.grid.worldmap_cell_size")?.Set(miles);

        // ---- Step: event feedback (one mode choice → events + log + SAPI voices). "Events" not "combat":
        //      damage / healing / spellcasts etc. fire in AND out of combat. ----

        private const string EnemySlot = "wizard.enemy_config"; // persisted ids of the wizard's two SAPI configs
        private const string AllySlot = "wizard.ally_config";
        private static readonly string[] SourceBuckets = { "party", "enemy", "neutral" };

        private static void BuildEventFeedbackStep(Container content)
        {
            content.Add(new TextElement(() => Loc.T("wizard.events.help")));
            var list = new ListContainer();
            list.Add(new ProxyRadioOption(() => Loc.T("wizard.events.positional"),
                () => CurrentMode() == "positional", ApplyPositional));
            list.Add(new ProxyRadioOption(() => Loc.T("wizard.events.screen_reader"),
                () => CurrentMode() == "screen_reader", ApplyScreenReader));
            list.Add(new ProxyRadioOption(() => Loc.T("wizard.events.log"),
                () => CurrentMode() == "log", ApplyLog));
            content.Add(list);
        }

        // Positional: events spoken spatially, with DISTINCT enemy vs ally voices for clarity; the duplicate
        // Combat + Magic game-log groups go off (the events cover them, incl. the spellcast events to come).
        // Neutrals share the ally voice (everything that isn't an enemy reads in the "ally" voice).
        private static void ApplyPositional() => ModSettings.Batch(() =>
        {
            var enemy = EnsureConfig(EnemySlot, "wizard.events.enemy_config_name");
            var ally = EnsureConfig(AllySlot, "wizard.events.ally_config_name");
            SetEventSource("enemy", enabled: true, config: enemy, positional: true);
            SetEventSource("party", enabled: true, config: ally, positional: true);
            SetEventSource("neutral", enabled: true, config: ally, positional: true);
            SetLogGroup("combat", false);
            SetLogGroup("magic", false);
        });

        // Screen reader: events through your normal (default) config, non-positional; Combat + Magic log
        // stays off (the events convey it, no need to double up).
        private static void ApplyScreenReader() => ModSettings.Batch(() =>
        {
            foreach (var b in SourceBuckets) SetEventSource(b, enabled: true, config: "default", positional: false);
            SetLogGroup("combat", false);
            SetLogGroup("magic", false);
        });

        // Game log: no event speech — the screen reader reads the game's own log instead, so Combat + Magic
        // log groups go on and the events go off (no duplication).
        private static void ApplyLog() => ModSettings.Batch(() =>
        {
            foreach (var b in SourceBuckets) SetEventSource(b, enabled: false, config: "default", positional: false);
            SetLogGroup("combat", true);
            SetLogGroup("magic", true);
        });

        // Which mode the live settings reflect (read off the enemy bucket) — a heuristic for the radio
        // "selected" marker + roadmap summary; the menu's per-source controls remain the source of truth.
        private static string CurrentMode()
        {
            var b = ModSettings.GetCategory("events.settings.enemy");
            if (!(b?.Get<BoolSetting>("enabled")?.Get() ?? true)) return "log";
            bool positional = b?.Get<BoolSetting>("positional")?.Get() ?? true;
            var cfg = b?.Get<ChoiceSetting>("speech_config")?.ValueId;
            return positional && !string.IsNullOrEmpty(cfg) && cfg != "default" ? "positional" : "screen_reader";
        }

        // Set one source bucket's global event output (the per-source defaults the events inherit).
        private static void SetEventSource(string bucket, bool enabled, string config, bool positional)
        {
            var b = ModSettings.GetCategory("events.settings." + bucket);
            b?.Get<BoolSetting>("enabled")?.Set(enabled);
            b?.Get<ChoiceSetting>("speech_config")?.Set(config);
            b?.Get<BoolSetting>("positional")?.Set(positional);
        }

        // Turn a whole log message group (combat / magic) on or off — the Default overlay's log defaults.
        private static void SetLogGroup(string group, bool on)
        {
            var cat = ModSettings.GetCategory("defaults.log." + group);
            if (cat == null) return;
            foreach (var child in cat.Children)
                if (child is BoolSetting b) b.Set(on);
        }

        // Reuse the slot's remembered SAPI config if it still exists, else create a fresh SAPI config and
        // remember its id (so re-runs reuse/re-tune the same enemy/ally voices, not pile up duplicates).
        private static string EnsureConfig(string slotPath, string nameKey)
        {
            var slot = ModSettings.GetSetting<StringSetting>(slotPath);
            var id = slot?.Get();
            if (!string.IsNullOrEmpty(id) && SpeechConfigRegistry.Ids().Contains(id)) return id;
            id = SpeechConfigRegistry.Add();
            SpeechConfigRegistry.SetName(id, Loc.T(nameKey));
            SpeechConfigRegistry.Get(id)?.Tree?.Get<ChoiceSetting>("handler")?.Set("sapi");
            slot?.Set(id);
            return id;
        }

        // ---- Steps: tune the enemy / ally voices (each config's own settings tree + a test line) ----

        private static void BuildEnemyVoiceStep(Container content)
            => BuildVoiceStep(content, EnemySlot, "wizard.events.enemy_voice_help");

        private static void BuildAllyVoiceStep(Container content)
            => BuildVoiceStep(content, AllySlot, "wizard.events.ally_voice_help");

        private static void BuildVoiceStep(Container content, string slotPath, string helpKey)
        {
            content.Add(new TextElement(() => Loc.T(helpKey)));
            var sapi = ConfigParams(slotPath);
            if (sapi != null) content.Add(SettingsTree(sapi.Children));
            content.Add(new ProxyActionButton(() => Loc.T("wizard.events.test"), null, () => TestVoice(slotPath)));
        }

        private static CategorySetting ConfigParams(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            return string.IsNullOrEmpty(id) ? null : SpeechConfigRegistry.Get(id)?.Tree?.Get<CategorySetting>("sapi");
        }

        private static void TestVoice(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            if (!string.IsNullOrEmpty(id)) SpeechConfigRegistry.Get(id)?.Speak(Loc.T("wizard.events.test_line"), interrupt: true);
        }

        // ---- navigation ----

        // The active step sequence (dynamic — the engine-settings step drops out for a paramless engine,
        // and future phases just append here).
        private static Step[] ActiveSteps()
        {
            var steps = new List<Step> { Step.Backend };
            if (HasHandlerSettings()) steps.Add(Step.HandlerSettings);
            steps.Add(Step.Navigation);
            steps.Add(Step.EventFeedback);
            if (CurrentMode() == "positional") { steps.Add(Step.EnemyVoice); steps.Add(Step.AllyVoice); } // tune the two SAPI voices
            return steps.ToArray();
        }

        protected override void OnBack() => GoTo(-1);
        protected override void OnNext() => GoTo(+1);

        // Step by ±1 through the active sequence; stepping off either end leaves the wizard.
        private static void GoTo(int delta)
        {
            var steps = ActiveSteps();
            int i = Array.IndexOf(steps, s_step) + delta;
            // Stepping forward off the end = Finish: play the same fanfare the chargen wizard plays on
            // completion. Backing off the front (i < 0) is a cancel, so no sound there.
            if (i >= steps.Length) { UiSound.Play(UISoundType.ChargenCompleteClick); Close(); return; }
            if (i < 0) { Close(); return; }
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
