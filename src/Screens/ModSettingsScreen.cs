using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The mod's settings screen — the tabbed category browser. Opened from the <see cref="ModMenuScreen"/>
    /// launcher (Settings entry), not bound directly to a key. Mod-pushed: <see cref="IsActive"/> reads a
    /// static flag <see cref="Open"/> sets. Two Tab-stops, mirroring the game's settings screen: a CATEGORIES
    /// tab list (Input, UI…), then a content region holding the selected category's settings treeview.
    /// Selecting a tab swaps only the content (tabs untouched, so tab focus survives) and the tab announces
    /// "selected" in place — no extra speech. Opening engages focus mode so it owns the keyboard everywhere;
    /// closing restores the prior state and reveals the launcher beneath it (it sits at a higher layer).
    /// Escape closes.
    /// </summary>
    public sealed class ModSettingsScreen : Screen
    {
        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.modsettings";
        public override string ScreenName => Message.Localized("ui", "screen.settings").Resolve();
        public override int Layer => 37; // above the mod-menu launcher (35), so it stacks on top of it
        public override bool IsActive() => s_open;

        private int _active;
        private int _builtActive;
        private bool _built;
        private Container _content; // wraps the active category's treeview; refilled on tab switch

        // Overlays tab: the live tree + the Add button + id→node map, so add/remove/reorder mutate the tree
        // in place (no destructive rebuild) and focus the affected node.
        private Container _overlaysTree;
        private UIElement _addButton;
        private readonly Dictionary<string, Container> _overlayNodes = new Dictionary<string, Container>();

        // Speech tab: the additional-speech-config subtree + its Add button + id→node map (same in-place
        // add/remove pattern as overlays).
        private Container _speechConfigsTree;
        private UIElement _speechAddButton;
        private readonly Dictionary<string, Container> _speechConfigNodes = new Dictionary<string, Container>();

        // Explicit tabs (the settings Root holds bindings/announcements/ui, which don't map 1:1 to tabs:
        // the UI tab composes the global announcement settings + the per-element-type overrides).
        // Alphabetical by label, so the tab list is easy to scan.
        private static readonly (string key, string label, string loc)[] Tabs =
        {
            ("audio", "Audio", "category.audio"),
            ("events", "Events", "category.events"),
            ("exploration", "Exploration", "category.exploration"),
            ("input", "Input", "category.input"),
            ("log", "Log", "category.log"),
            ("overlays", "Overlays", "category.overlays"),
            ("scanner", "Scanner", "category.scanner"),
            ("speech", "Speech", "category.speech"),
            ("ui", "UI", "category.ui"),
        };

        // Focus mode is owned by the ModMenuScreen launcher (always open beneath us), so we don't touch it.
        public override void OnPush() { _active = 0; _built = false; }
        public override void OnPop() { Clear(); _content = null; }

        public override void OnUpdate()
        {
            if (!_built) { Build(); return; }
            if (_active != _builtActive) RebuildContent(); // tab changed → refill content only
        }

        // Escape closes the whole menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }

        // Localized menu string ("settings" table) with the English fallback.
        private static string Loc(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);

        private void Build()
        {
            _built = true;
            Clear();

            var tabs = new ListContainer(Loc("menu.categories", "Categories"));
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                tabs.Add(new ProxyTab(Loc(Tabs[i].loc, Tabs[i].label), () => _active == idx, () => _active = idx));
            }
            Add(tabs);

            _content = new Panel(); // structural wrapper; the tree inside it is the second Tab-stop
            Add(_content);
            RebuildContent();

            // Two standing Tab-stops after the tree (NOT inside _content, so they survive tab switches
            // and focus stays put after a reset): reset THIS tab, and reset everything.
            Add(new ProxyActionButton(
                () => Message.Localized("settings", "reset.tab", new { name = ActiveTabLabel() }).Resolve(),
                () => true, ResetActiveTab));
            Add(new ProxyActionButton(
                () => Message.Localized("settings", "reset.all").Resolve(),
                () => true, ResetAllSettings));

            Navigation.Attach(this);
            Tts.Speak(Loc("menu.title", "Mod menu")); // once, on open (Build runs only on open now)
            Navigation.AnnounceCurrent();
        }

        // Refill ONLY the content wrapper (tabs untouched) so focus on the tab list survives a switch — the
        // new tree (and its label) replaces the old; the user Tabs into it.
        private void RebuildContent()
        {
            _builtActive = _active;
            if (_content == null) return;
            _content.Clear();
            _overlaysTree = null; _overlayNodes.Clear(); // (re)set when the overlays tab builds
            _speechConfigsTree = null; _speechConfigNodes.Clear(); // (re)set when the speech tab builds
            // Unlabeled = the structural tree root (silent, never focused as a node); only real sub-groups
            // announce expand/collapse. The category is already conveyed by the selected tab.
            var tree = new TreeGroup();
            BuildTab(tree, _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null);
            _content.Add(tree);
        }

        private void BuildTab(TreeGroup tree, string key)
        {
            if (key == "input")
            {
                var bindings = ModSettings.Root.Get<CategorySetting>("bindings");
                if (bindings != null)
                    foreach (var s in bindings.Children) BuildSettingNode(tree, s);
            }
            else if (key == "ui")
            {
                var annRoot = ModSettings.Root.Get<CategorySetting>("announcements");

                // Announcements: a verbosity preset + a plain on/off per announcement type — the 90%
                // case, first. The full per-type detail (suffix etc.) lives under Per-element overrides.
                if (annRoot != null)
                {
                    var announcements = new TreeGroup(Loc("ui.announcements", "Announcements"));
                    announcements.Add(BuildVerbosityDropdown(annRoot));
                    foreach (var child in annRoot.Children)
                    {
                        if (!(child is CategorySetting annCat) || annCat.Hidden) continue;
                        var enabled = annCat.Get<BoolSetting>("enabled");
                        if (enabled != null)
                            announcements.Add(new ProxyBoolToggle(annCat.Label, enabled.Get,
                                () => enabled.Set(!enabled.Get())));
                    }
                    tree.Add(announcements);
                }

                // (future root-level UI settings slot in here)

                // Per-element overrides, tucked at the bottom: the Global node carries each announcement
                // type's FULL settings (suffix punctuation etc.); then every element type's tri-state
                // inherit/on/off overrides, alphabetical.
                var overrides = new TreeGroup(Loc("ui.element_overrides", "Per-element overrides"));
                if (annRoot != null)
                {
                    var global = new TreeGroup(Loc("global.group", "Global"));
                    foreach (var s in annRoot.Children) BuildSettingNode(global, s);
                    if (global.Children.Count > 0) overrides.Add(global);
                }
                var ui = ModSettings.Root.Get<CategorySetting>("ui");
                if (ui != null)
                    foreach (var s in ui.Children.OrderBy(c => c.Label, System.StringComparer.CurrentCultureIgnoreCase))
                        BuildSettingNode(overrides, s);
                if (overrides.Children.Count > 0) tree.Add(overrides);
            }
            else if (key == "overlays")
            {
                // Each overlay is a root node (Cursor + systems + Make-standard + Remove); the Add button at
                // the bottom appends one. Add/remove/reorder mutate this live tree in place — see
                // BuildOverlayNode / AddOverlay / RemoveOverlay / MakeStandard (no destructive rebuild).
                _overlaysTree = tree;
                var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
                foreach (var id in WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayIds())
                {
                    var oc = overlays?.Get<CategorySetting>(id);
                    if (oc != null) tree.Add(BuildOverlayNode(oc, id));
                }
                _addButton = new ProxyActionButton(Loc("overlay.add", "Add overlay"), null, AddOverlay);
                tree.Add(_addButton);
            }
            else if (key == "audio")
            {
                // Flat: master volume, then every system volume at the root (they're all just volumes —
                // no "System volumes" grouping; the storage paths under audio.volumes.* are unchanged).
                var audio = ModSettings.Root.Get<CategorySetting>("audio");
                if (audio != null)
                    foreach (var s in audio.Children)
                    {
                        if (s is CategorySetting volumes && volumes.Key == "volumes")
                            foreach (var v in volumes.Children) BuildSettingNode(tree, v);
                        else
                            BuildSettingNode(tree, s);
                    }
            }
            else if (key == "speech")
            {
                // The DEFAULT config (handler dropdown + each handler's subtree) first, then the advanced
                // "Additional speech configurations" node — each config a clone of the same schema with
                // Rename/Remove, and an Add button at the bottom (in-place add/remove like overlays).
                var speech = ModSettings.Root.Get<CategorySetting>("speech");
                if (speech != null)
                    foreach (var s in speech.Children)
                    {
                        if (s is CategorySetting cs && cs.Key == "additional") continue; // rendered specially below
                        BuildSettingNode(tree, s);
                    }

                var configs = new TreeGroup(Loc("speech.additional", "Additional speech configurations"));
                _speechConfigsTree = configs;
                foreach (var id in WrathAccess.Speech.SpeechConfigRegistry.Ids())
                {
                    var cc = SpeechConfigCat(id);
                    if (cc != null) configs.Add(BuildSpeechConfigNode(cc, id));
                }
                _speechAddButton = new ProxyActionButton(Loc("speech.config.add", "Add speech configuration"), null, AddSpeechConfig);
                configs.Add(_speechAddButton);
                tree.Add(configs);
            }
            else if (key == "events")
            {
                // Two nodes, both rendered generically: "Event settings" (per-source global defaults) and
                // "Individual event customization" (per-event, per-source, inheriting those globals).
                var events = ModSettings.Root.Get<CategorySetting>("events");
                if (events != null)
                    foreach (var s in events.Children) BuildSettingNode(tree, s);
            }
            else if (key == "scanner")
            {
                // One unified system: per-entity-type settings (sound + announcements, mirroring the
                // taxonomy) under Entities, then the sonar's own tunables under Sonar.
                var entities = new TreeGroup(Loc("scanner.entities", "Entities"));

                // The global announcement base every entity inherits.
                var paRoot = ModSettings.Root.Get<CategorySetting>("proxy_announce");
                if (paRoot != null)
                {
                    var defaults = new TreeGroup(Loc("scanner.ann_defaults", "Announcement defaults"));
                    foreach (var p in paRoot.Children)
                    {
                        // One-toggle parts read flat; the spatial part (sub-toggles) keeps its subgroup.
                        if (p is CategorySetting pc && pc.Children.Count == 1
                            && pc.Get<BoolSetting>("enabled") is BoolSetting en)
                            defaults.Add(new ProxyBoolToggle(pc.Label, en.Get, () => en.Set(!en.Get())));
                        else
                            BuildSettingNode(defaults, p);
                    }
                    if (defaults.Children.Count > 0) entities.Add(defaults);
                }

                foreach (var cat in WrathAccess.Exploration.ScanTaxonomy.Categories)
                    BuildEntityNode(cat, entities);
                // World-map entity types (separate taxonomy) share this tree — same sound dropdowns.
                foreach (var cat in WrathAccess.Exploration.GlobalMapTaxonomy.Categories)
                    BuildEntityNode(cat, entities);
                tree.Add(entities);

                var sonar = new TreeGroup(Loc("category.sonar", "Sonar"));
                var d = SystemDefaults("sonar");
                if (d != null) foreach (var s in d.Children) BuildSettingNode(sonar, s);
                if (sonar.Children.Count > 0) tree.Add(sonar);
            }
            else if (key == "log")
            {
                // The shared log message-type tree, configured once for all overlays.
                var d = SystemDefaults("log");
                if (d != null)
                    foreach (var s in d.Children) BuildSettingNode(tree, s);
            }
            else if (key == "exploration")
            {
                // The Default overlay's cursor (mode + speed per slot) first, then the remaining shared
                // system defaults, one collapsible node per system (empty ones — systems with no
                // tunables — are skipped by BuildSettingNode).
                var cursor = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor");
                if (cursor != null) BuildSettingNode(tree, cursor);
                foreach (var sysKey in new[] { "grid", "spatial", "slope", "walltones", "object", "fog", "path" })
                {
                    var d = SystemDefaults(sysKey);
                    if (d != null) BuildSettingNode(tree, d);
                }
            }
        }

        // Verbosity presets: each names the announcement types it turns OFF (everything else on).
        // The dropdown derives its state from the live toggles — hand-edits read as "Custom".
        private static readonly (string label, string loc, string[] off)[] VerbosityPresets =
        {
            ("Verbose", "preset.verbose", new string[0]),
            ("Standard", "preset.standard", new[] { "position" }),
            ("Concise", "preset.concise", new[] { "role", "tooltip", "position" }),
        };

        private UIElement BuildVerbosityDropdown(CategorySetting annRoot)
        {
            var visible = annRoot.Children.OfType<CategorySetting>().Where(c => !c.Hidden)
                .Select(c => (Key: c.Key, Enabled: c.Get<BoolSetting>("enabled")))
                .Where(t => t.Enabled != null).ToList();

            var labels = VerbosityPresets.Select(p => Loc(p.loc, p.label)).ToList();
            labels.Add(Loc("preset.custom", "Custom"));

            int Current()
            {
                for (int i = 0; i < VerbosityPresets.Length; i++)
                {
                    var off = VerbosityPresets[i].off;
                    bool match = true;
                    foreach (var t in visible)
                        if (t.Enabled.Get() == System.Array.IndexOf(off, t.Key) >= 0) { match = false; break; }
                    if (match) return i;
                }
                return VerbosityPresets.Length; // Custom
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx >= VerbosityPresets.Length) return; // choosing Custom = keep as-is
                var off = VerbosityPresets[idx].off;
                foreach (var t in visible)
                    t.Enabled.Set(System.Array.IndexOf(off, t.Key) < 0);
            }

            // "Custom" is a derived display state, not a choice — mark it virtual.
            return new ProxyChoiceDropdown(Loc("ui.verbosity", "Verbosity"), labels, Current, Apply,
                selectableCount: VerbosityPresets.Length);
        }

        private static CategorySetting SystemDefaults(string key)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>(key);

        // One system inside an overlay: enabled toggle + whole-subtree inheritance. "Following
        // defaults" shows just a Customize button (the tunables live on the Sonar/Log/Exploration
        // tabs); Customize materializes the overlay's own full copy of the system tree (seeded from
        // the current defaults) in place, and Reset drops it again. The node label carries the live
        // state so the deviation is audible at a glance.
        private Container BuildSystemNode(string id, CategorySetting sysCat)
        {
            var key = sysCat.Key;
            var node = new TreeGroup("")
            {
                LabelProvider = () => Loc("system." + key,
                        WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(key))
                    + ", " + (WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.IsCustomized(id, key)
                        ? Loc("overlay.state_customized", "customized")
                        : Loc("overlay.state_default", "following defaults"))
            };
            FillSystemNode(node, id, key, sysCat);
            return node;
        }

        private void FillSystemNode(Container node, string id, string key, CategorySetting sysCat)
        {
            node.Clear();
            var enabled = sysCat.Get<BoolSetting>("enabled");
            if (enabled != null)
                node.Add(new ProxyBoolToggle(enabled.Label, enabled.Get, () => enabled.Set(!enabled.Get())));

            if (WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.IsCustomized(id, key))
            {
                var custom = sysCat.Get<CategorySetting>("custom");
                if (custom != null)
                    foreach (var c in custom.Children) BuildSettingNode(node, c);
                node.Add(new ProxyActionButton(Loc("overlay.reset_defaults", "Reset to defaults"), null, () =>
                {
                    WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.ResetSystem(id, key);
                    FillSystemNode(node, id, key, sysCat);
                    Tts.Speak(Loc("overlay.state_default", "following defaults"));
                    Navigation.Focus(node, announce: false);
                }));
            }
            else
            {
                node.Add(new ProxyActionButton(Loc("overlay.customize", "Customize for this overlay"), null, () =>
                {
                    WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Customize(id, key);
                    FillSystemNode(node, id, key, sysCat);
                    Tts.Speak(Loc("overlay.state_customized", "customized"));
                    Navigation.Focus(node, announce: false);
                }));
            }
        }

        // One overlay = a collapsible node (Cursor + systems) with a LIVE "(standard)" tag, plus always-on
        // Make-standard + Remove actions. Returned (not added) so the caller can add or insert it.
        private Container BuildOverlayNode(CategorySetting oCat, string id)
        {
            var group = new TreeGroup("")
            {
                LabelProvider = () => WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayName(id)
            };
            foreach (var c in oCat.Children) // hidden "name" is skipped
            {
                if (c is CategorySetting sysCat
                    && WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(sysCat.Key) != null)
                    group.Add(BuildSystemNode(id, sysCat));
                else
                    BuildSettingNode(group, c);
            }
            group.Add(new ProxyActionButton(Loc("overlay.rename", "Rename overlay"), null, () => RenameOverlay(id)));
            group.Add(new ProxyActionButton(Loc("overlay.remove", "Remove overlay"), null, () => RemoveOverlay(id)));
            _overlayNodes[id] = group;
            return group;
        }

        // ---- incremental overlay add / remove / reorder (mutate the live tree, no rebuild) ----

        private void AddOverlay()
        {
            var id = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Add();
            var oc = ModSettings.Root.Get<CategorySetting>("overlays")?.Get<CategorySetting>(id);
            if (oc == null || _overlaysTree == null) return;
            var node = BuildOverlayNode(oc, id);
            int at = _addButton != null ? _overlaysTree.IndexOf(_addButton) : _overlaysTree.Children.Count;
            _overlaysTree.Insert(at, node);
            Tts.Speak(Loc("overlay.added", "Overlay added"));
            Navigation.Focus(node);
        }

        private string ActiveTabLabel()
            => _active >= 0 && _active < Tabs.Length ? Loc(Tabs[_active].loc, Tabs[_active].label) : "";

        // The settings subtrees each tab renders — what its Reset button restores. Overlays are special:
        // reset = remove every overlay (back to the empty default list); the shared system defaults they
        // followed live on the other tabs.
        private static string[] ResetRootsFor(string key)
        {
            switch (key)
            {
                case "audio": return new[] { "audio" };
                case "exploration": return new[]
                {
                    "defaults.cursor", "defaults.grid", "defaults.spatial", "defaults.slope",
                    "defaults.walltones", "defaults.object", "defaults.fog", "defaults.path",
                };
                case "input": return new[] { "bindings" };
                case "log": return new[] { "defaults.log" };
                case "scanner": return new[] { "defaults.sonar", "sounds", "proxy_announce", "proxy_elem" };
                case "speech": return new[] { "speech" };
                case "ui": return new[] { "announcements", "ui" };
                default: return new string[0];
            }
        }

        private void ResetActiveTab()
        {
            var key = _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null;
            if (key == null) return;
            ModSettings.Batch(() =>
            {
                if (key == "overlays") RemoveAllOverlays();
                else
                    foreach (var path in ResetRootsFor(key))
                        ModSettings.GetCategory(path)?.ResetToDefault();
            });
            RebuildContent(); // values are read live, but structural tabs (overlays/sounds) need the refresh
            Tts.Speak(Message.Localized("settings", "reset.tab_done", new { name = ActiveTabLabel() }).Resolve());
        }

        private void ResetAllSettings()
        {
            ModSettings.Batch(() =>
            {
                RemoveAllOverlays();
                foreach (var s in ModSettings.Root.Children) s.ResetToDefault();
            });
            RebuildContent();
            Tts.Speak(Message.Localized("settings", "reset.all_done").Resolve());
        }

        private static void RemoveAllOverlays()
        {
            var ids = new List<string>(WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayIds());
            foreach (var id in ids) WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Remove(id);
        }

        private void RemoveOverlay(string id)
        {
            if (_overlaysTree == null || !_overlayNodes.TryGetValue(id, out var node)) return;
            if (!WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Remove(id)) return;
            int idx = _overlaysTree.IndexOf(node);
            _overlaysTree.Remove(node);
            _overlayNodes.Remove(id);
            var kids = _overlaysTree.Children;
            UIElement target = kids.Count == 0 ? null : kids[idx < kids.Count ? idx : kids.Count - 1];
            Tts.Speak(Loc("overlay.removed", "Overlay removed"));
            Navigation.Focus(target);
        }

        private void RenameOverlay(string id)
        {
            var current = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayName(id);
            ModTextEntryScreen.Open(Loc("overlay.rename", "Rename overlay"), current, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SetOverlayName(id, name);
                // The node's label is a live LabelProvider, so it already reflects the new name.
                Tts.Speak(Loc("overlay.renamed", "Renamed to") + " " + name);
            });
        }

        // ---- additional speech configs (same in-place add / remove / rename as overlays) ----

        private static CategorySetting SpeechConfigCat(string id)
            => ModSettings.Root.Get<CategorySetting>("speech")?.Get<CategorySetting>("additional")?.Get<CategorySetting>(id);

        // One config = a collapsible node (the cloned handler/params schema; the hidden name is skipped)
        // with Rename + Remove. Returned (not added) so the caller can add or insert it.
        private Container BuildSpeechConfigNode(CategorySetting cc, string id)
        {
            var group = new TreeGroup(WrathAccess.Speech.SpeechConfigRegistry.Name(id))
            {
                LabelProvider = () => WrathAccess.Speech.SpeechConfigRegistry.Name(id)
            };
            foreach (var s in cc.Children) BuildSettingNode(group, s); // the hidden "name" is skipped
            group.Add(new ProxyActionButton(Loc("speech.config.rename", "Rename"), null, () => RenameSpeechConfig(id)));
            group.Add(new ProxyActionButton(Loc("speech.config.remove", "Remove"), null, () => RemoveSpeechConfig(id)));
            _speechConfigNodes[id] = group;
            return group;
        }

        private void AddSpeechConfig()
        {
            var id = WrathAccess.Speech.SpeechConfigRegistry.Add();
            var cc = SpeechConfigCat(id);
            if (cc == null || _speechConfigsTree == null) return;
            var node = BuildSpeechConfigNode(cc, id);
            int at = _speechAddButton != null ? _speechConfigsTree.IndexOf(_speechAddButton) : _speechConfigsTree.Children.Count;
            _speechConfigsTree.Insert(at, node);
            Tts.Speak(Loc("speech.config.added", "Speech configuration added"));
            Navigation.Focus(node);
        }

        private void RemoveSpeechConfig(string id)
        {
            if (_speechConfigsTree == null || !_speechConfigNodes.TryGetValue(id, out var node)) return;
            if (!WrathAccess.Speech.SpeechConfigRegistry.Remove(id)) return;
            int idx = _speechConfigsTree.IndexOf(node);
            _speechConfigsTree.Remove(node);
            _speechConfigNodes.Remove(id);
            var kids = _speechConfigsTree.Children;
            UIElement target = kids.Count == 0 ? null : kids[idx < kids.Count ? idx : kids.Count - 1];
            Tts.Speak(Loc("speech.config.removed", "Speech configuration removed"));
            Navigation.Focus(target);
        }

        private void RenameSpeechConfig(string id)
        {
            var current = WrathAccess.Speech.SpeechConfigRegistry.Name(id);
            ModTextEntryScreen.Open(Loc("speech.config.rename", "Rename"), current, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                WrathAccess.Speech.SpeechConfigRegistry.SetName(id, name); // node label is a live LabelProvider
                Tts.Speak(Loc("overlay.renamed", "Renamed to") + " " + name);
            });
        }


        // One entity node of the Scanner tab's Entities tree: its sound dropdown + an Announcements
        // subgroup (the per-part tri-states), then its subcategories recursively. Mirrors ScanTaxonomy.
        private static void BuildEntityNode(WrathAccess.Exploration.ScanTaxonomy.Node node, Container parent)
        {
            var group = new TreeGroup(Loc(node.LocKey, node.Label));

            var sound = WrathAccess.Exploration.ScanSounds.SoundSetting(node.Key);
            if (sound != null)
                group.Add(new ProxyChoiceDropdown(Loc("scanner.sound", "Sound"),
                    sound.Choices.Select(ch => ch.Label).ToList(),
                    () => IndexOfChoice(sound),
                    idx => { if (idx >= 0 && idx < sound.Choices.Count) sound.Set(sound.Choices[idx].Id); }));

            var annCat = ModSettings.GetCategory("proxy_elem." + node.Key);
            if (annCat != null)
            {
                var ann = new TreeGroup(Loc("scanner.announcements", "Announcements"));
                foreach (var c in annCat.Children)
                    if (c is NullableBoolSetting nb) ann.Add(new ProxyOverrideToggle(nb));
                if (ann.Children.Count > 0) group.Add(ann);
            }

            foreach (var child in node.Children) BuildEntityNode(child, group);
            parent.Add(group);
        }

        // Map a setting to a navigable control; categories recurse into collapsible tree groups.
        // Internal so other screens (e.g. the setup wizard) render the same controls over a subtree.
        internal static void BuildSettingNode(Container parent, Setting s)
        {
            if (s.Hidden) return; // hidden globals (no [ShowInGlobalSettings]) + hidden state settings
            switch (s)
            {
                case CategorySetting cat:
                    var group = new TreeGroup(cat.Label);
                    foreach (var c in cat.Children) BuildSettingNode(group, c);
                    if (group.Children.Count > 0) parent.Add(group); // skip empty groups
                    break;
                case BindingSetting bs:
                    parent.Add(new ProxyModBinding(bs.Action));
                    break;
                case BoolSetting b:
                    parent.Add(new ProxyBoolToggle(b.Label, b.Get, () => b.Set(!b.Get())));
                    break;
                case NullableBoolSetting nb:
                    parent.Add(new ProxyOverrideToggle(nb));
                    break;
                case NullableIntSetting ni:
                    parent.Add(new ProxyNullableIntSetting(ni));
                    break;
                case IntSetting i:
                    parent.Add(new ProxyIntSetting(i));
                    break;
                case ChoiceSetting c:
                    parent.Add(new ProxyChoiceDropdown(c.Label,
                        c.Choices.Select(ch => ch.Label).ToList(),
                        () => IndexOfChoice(c),
                        idx => { if (idx >= 0 && idx < c.Choices.Count) c.Set(c.Choices[idx].Id); },
                        inheritedValue: c.InheritedValue));
                    break;
            }
        }

        private static int IndexOfChoice(ChoiceSetting c)
        {
            for (int i = 0; i < c.Choices.Count; i++)
                if (c.Choices[i].Id == c.ValueId) return i;
            return -1;
        }
    }
}
