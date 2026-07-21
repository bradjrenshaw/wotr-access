using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The SIMPLE settings screen — what the mod-menu "Settings" entry opens. A single small tree of
    /// task-level controls a new user can face: Speech, per-sound-system nodes (Active when + Volume,
    /// extensible later), preset dropdowns for "how much does it talk", movement basics, and the Input
    /// bindings verbatim (already simple). Every control is a VIEW over the same Setting objects the
    /// full screen edits — composites use the derived-state preset idiom (read "Custom" when the
    /// fine-grained state matches no preset; picking a preset batch-writes it), so there is no second
    /// source of truth and hand-tuning in the full screen is never lost or lied about. The full
    /// tabbed browser stays untouched behind the "All settings" button (its own screen, stacking
    /// above). Curation rule: new settings land in the full screen by default and are promoted here
    /// only deliberately.
    /// </summary>
    public sealed class BasicSettingsScreen : Screen
    {
        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.modsettings.basic";
        public override string ScreenName => Message.Localized("ui", "screen.settings").Resolve();
        public override int Layer => 37; // above the launcher (35); the full settings screen (39) stacks on top
        public override bool IsActive() => s_open;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }

        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);

        // The sound systems surfaced as basic nodes — exactly the ones with a volume slider.
        private static readonly string[] SoundSystems = { "sonar", "slope", "walltones", "fog", "object" };

        public override void Build(GraphBuilder b)
        {
            const string k = "basicset:";
            b.BeginStop("content");

            // ---- Speech: the default config, exactly as the full tab renders it (minus extra configs).
            var speech = ModSettings.Root.Get<CategorySetting>("speech");
            if (speech != null)
            {
                b.BeginGroup(ControlId.Structural(k + "speech"),
                    GraphNodes.Group(() => L("category.speech", "Speech")));
                foreach (var s in speech.Children)
                {
                    if (s is CategorySetting cs && cs.Key == "additional") continue; // advanced-only
                    ModSettingNodes.Emit(b, s, k + "speech.");
                }
                b.EndGroup();
            }

            // ---- Audio cues: master volume first, then one node per audible system — Active when +
            // Volume, with room to grow.
            b.BeginGroup(ControlId.Structural(k + "sounds"),
                GraphNodes.Group(() => L("basic.audio_cues", "Audio cues")));
            var master = ModSettings.GetSetting<IntSetting>("audio.master_volume");
            if (master != null)
                b.AddItem(ControlId.Structural(k + "snd.master"), ModSettingNodes.IntSlider(master));
            foreach (var sys in SoundSystems)
            {
                var mode = ModSettingsScreen.SystemDefaults(sys)?.Get<ChoiceSetting>("mode");
                var volume = ModSettings.Root.Get<CategorySetting>("audio")
                    ?.Get<CategorySetting>("volumes")?.Get<IntSetting>(sys);
                if (mode == null && volume == null) continue;
                var name = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(sys);
                b.BeginGroup(ControlId.Structural(k + "snd." + sys),
                    GraphNodes.Group(() => L("system." + sys, name)));
                if (mode != null)
                    b.AddItem(ControlId.Structural(k + "snd." + sys + ".mode"),
                        ModSettingNodes.ChoiceSettingDropdown(mode));
                if (volume != null)
                    b.AddItem(ControlId.Structural(k + "snd." + sys + ".volume"),
                        ModSettingNodes.IntSlider(volume));
                // The sonar's node also carries the per-entity-type Play-sound switches — what pings,
                // by the labels a new user would look for (the old workflow was hunting for "Silent"
                // in a sound dropdown). Listed switches, picks and subtype overrides stay advanced.
                if (sys == "sonar")
                {
                    ScanToggle(b, k, "units.enemies", "basic.scan.enemies", "Enemy units");
                    ScanToggle(b, k, "units.neutrals", "basic.scan.neutrals", "Neutral units");
                    ScanToggle(b, k, "units.party", "basic.scan.party", "Ally units");
                    ScanToggle(b, k, "containers", "taxonomy.containers", "Containers");
                    ScanToggle(b, k, "doors", "taxonomy.doors", "Doors");
                    ScanToggle(b, k, "exits", "taxonomy.exits", "Exits");
                    ScanToggle(b, k, "searchpoints", "taxonomy.searchpoints", "Search points");
                    ScanToggle(b, k, "traps", "taxonomy.traps", "Traps");
                    ScanToggle(b, k, "trapzones", "taxonomy.trapzones", "Trap zones");
                    ScanToggle(b, k, "mechanisms", "taxonomy.mechanisms", "Mechanisms");
                    ScanToggle(b, k, "hazards", "taxonomy.hazards", "Hazards");
                    ScanToggle(b, k, "buffzones", "taxonomy.buffzones", "Buff zones");
                }
                b.EndGroup();
            }
            b.EndGroup();

            // ---- Announcements: "how much does it talk", all preset dropdowns.
            b.BeginGroup(ControlId.Structural(k + "ann"),
                GraphNodes.Group(() => L("basic.announcements", "Announcements")));
            var annRoot = ModSettings.Root.Get<CategorySetting>("announcements");
            // Contextful labels — inside the full screen's tabs the bare "Verbosity"/"Preset" labels
            // work because the tab names them; in this flat list they'd read label-less.
            if (annRoot != null)
                b.AddItem(ControlId.Structural(k + "ann.verbosity"),
                    ModSettingsScreen.BuildVerbosityDropdown(annRoot,
                        L("basic.menu_verbosity", "Menu verbosity")));
            b.AddItem(ControlId.Structural(k + "ann.scandetail"), BuildScanDetailDropdown());
            var log = ModSettingsScreen.SystemDefaults("log");
            if (log != null)
                b.AddItem(ControlId.Structural(k + "ann.logpreset"),
                    ModSettingsScreen.BuildLogPresetDropdown(log,
                        L("basic.log_detail", "Log detail")));
            b.AddItem(ControlId.Structural(k + "ann.events"), BuildEventLevelDropdown());
            b.EndGroup();

            // ---- Cursor: movement speed and the cursor-driven readouts.
            b.BeginGroup(ControlId.Structural(k + "move"),
                GraphNodes.Group(() => L("basic.cursor", "Cursor")));
            var cursor = ModSettingsScreen.SystemDefaults("cursor");
            var speed = cursor?.Get<CategorySetting>("primary")?.Get<IntSetting>("speed");
            if (speed != null)
                b.AddItem(ControlId.Structural(k + "move.speed"), ModSettingNodes.IntSlider(speed));
            var rooms = cursor?.Get<BoolSetting>("announce_rooms");
            if (rooms != null) ModSettingNodes.Emit(b, rooms, k + "move.");
            EmitSystemToggle(b, k, "path");
            EmitSystemToggle(b, k, "aoe");
            b.EndGroup();

            // ---- Input: the bindings, verbatim (the full tab shows exactly the same tree).
            var bindings = ModSettings.Root.Get<CategorySetting>("bindings");
            if (bindings != null)
            {
                b.BeginGroup(ControlId.Structural(k + "input"),
                    GraphNodes.Group(() => L("category.input", "Input")));
                foreach (var s in bindings.Children) ModSettingNodes.Emit(b, s, k + "input.");
                b.EndGroup();
            }

            // ---- The escape hatch: everything, in the full tabbed browser.
            b.AddItem(ControlId.Structural(k + "all"),
                GraphNodes.Button(() => L("basic.all_settings", "All settings"), ModSettingsScreen.Open));
        }

        // One entity type's Play-sound switch as a plain toggle over its resolved value — subcategory
        // settings write an explicit override (the advanced tri-state can still Reset to inherit).
        private static void ScanToggle(GraphBuilder b, string k, string node, string loc, string fallback)
        {
            var s = WrathAccess.Exploration.ScanEnabled.SoundEnabledSetting(node);
            if (s == null) return;
            System.Func<string> label = () => L(loc, fallback);
            var id = ControlId.Structural(k + "scan." + node);
            if (s is BoolSetting bs)
                b.AddItem(id, GraphNodes.Toggle(label, bs.Get, () => bs.Set(!bs.Get())));
            else if (s is NullableBoolSetting nb)
                b.AddItem(id, GraphNodes.Toggle(label, () => nb.Resolved, nb.ToggleExplicit));
        }

        // A speech-driven system (path info / AoE preview) as a plain on/off — writes its Active-when
        // mode (off <-> continuous; these systems support no "when moving").
        private static void EmitSystemToggle(GraphBuilder b, string k, string sys)
        {
            var mode = ModSettingsScreen.SystemDefaults(sys)?.Get<ChoiceSetting>("mode");
            if (mode == null) return;
            var name = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(sys);
            b.AddItem(ControlId.Structural(k + "move." + sys),
                GraphNodes.Toggle(() => L("system." + sys, name),
                    () => mode.ValueId != "off",
                    () => mode.Set(mode.ValueId != "off" ? "off" : "continuous")));
        }

        // Scanner detail tiers over the GLOBAL announcement-part toggles (proxy_announce.<part>.enabled).
        // Minimal = name + location; Standard = everything except the current-action part; Everything =
        // all parts. Per-entity-type overrides and the spatial sub-toggles are advanced-only and never
        // touched — hand-tuned states read back as "Custom".
        private static readonly (string label, string loc, string[] on)[] ScanDetailPresets =
        {
            ("Minimal", "preset.minimal", new[] { "name", "spatial" }),
            ("Standard", "preset.standard",
                new[] { "name", "interaction", "type", "hp", "condition", "check", "object_state", "spatial" }),
            ("Everything", "preset.everything",
                new[] { "name", "interaction", "type", "action", "hp", "condition", "check", "object_state", "spatial" }),
        };

        private static NodeVtable BuildScanDetailDropdown()
        {
            var parts = new List<(string Key, BoolSetting Enabled)>();
            var root = ModSettings.Root.Get<CategorySetting>("proxy_announce");
            if (root != null)
                foreach (var c in root.Children)
                    if (c is CategorySetting pc && pc.Get<BoolSetting>("enabled") is BoolSetting en)
                        parts.Add((pc.Key, en));

            var labels = ScanDetailPresets.Select(p => L(p.loc, p.label)).ToList();
            labels.Add(L("preset.custom", "Custom"));

            int Current()
            {
                for (int i = 0; i < ScanDetailPresets.Length; i++)
                {
                    var on = ScanDetailPresets[i].on;
                    bool match = true;
                    foreach (var p in parts)
                        if (p.Enabled.Get() != (System.Array.IndexOf(on, p.Key) >= 0)) { match = false; break; }
                    if (match) return i;
                }
                return ScanDetailPresets.Length; // Custom
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx >= ScanDetailPresets.Length) return;
                var on = ScanDetailPresets[idx].on;
                ModSettings.Batch(() =>
                {
                    foreach (var p in parts) p.Enabled.Set(System.Array.IndexOf(on, p.Key) >= 0);
                });
            }

            return ModSettingNodes.ChoiceDropdown(L("basic.scanner_detail", "Scanner detail"),
                labels, Current, Apply, selectableCount: ScanDetailPresets.Length);
        }

        // Event speech levels over the per-source Announce toggles (events.settings.<bucket>.enabled).
        // The axis new users think in is WHO the event is about; per-event customization stays advanced.
        private static readonly (string label, string loc, string[] on)[] EventLevels =
        {
            ("Off", "basic.ev.off", new string[0]),
            ("Party only", "basic.ev.party", new[] { "party" }),
            ("Party and enemies", "basic.ev.party_enemies", new[] { "party", "enemy" }),
            ("Everything", "basic.ev.all", new[] { "party", "enemy", "neutral", "unitless" }),
        };

        private static NodeVtable BuildEventLevelDropdown()
        {
            var buckets = new List<(string Key, BoolSetting Enabled)>();
            var settings = ModSettings.Root.Get<CategorySetting>("events")?.Get<CategorySetting>("settings");
            if (settings != null)
                foreach (var c in settings.Children)
                    if (c is CategorySetting bc && bc.Get<BoolSetting>("enabled") is BoolSetting en)
                        buckets.Add((bc.Key, en));

            var labels = EventLevels.Select(p => L(p.loc, p.label)).ToList();
            labels.Add(L("preset.custom", "Custom"));

            int Current()
            {
                for (int i = 0; i < EventLevels.Length; i++)
                {
                    var on = EventLevels[i].on;
                    bool match = true;
                    foreach (var p in buckets)
                        if (p.Enabled.Get() != (System.Array.IndexOf(on, p.Key) >= 0)) { match = false; break; }
                    if (match) return i;
                }
                return EventLevels.Length; // Custom
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx >= EventLevels.Length) return;
                var on = EventLevels[idx].on;
                ModSettings.Batch(() =>
                {
                    foreach (var p in buckets) p.Enabled.Set(System.Array.IndexOf(on, p.Key) >= 0);
                });
            }

            return ModSettingNodes.ChoiceDropdown(L("basic.event_speech", "Event speech"),
                labels, Current, Apply, selectableCount: EventLevels.Length);
        }
    }
}
