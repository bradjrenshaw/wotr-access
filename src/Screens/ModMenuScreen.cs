using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The mod's own settings menu (Ctrl+M, available everywhere). Mod-pushed: <see cref="IsActive"/> reads
    /// a static flag the toggle sets. Two Tab-stops, mirroring the game's settings screen: a CATEGORIES tab
    /// list (Input, UI), then a content region holding the selected category's settings treeview. Selecting
    /// a tab swaps only the content (tabs untouched, so tab focus survives) and the tab announces "selected"
    /// in place — no extra speech. Opening engages focus mode so the menu owns the keyboard everywhere;
    /// closing restores the prior state. Escape closes.
    /// </summary>
    public sealed class ModMenuScreen : Screen
    {
        private static bool s_open;
        public static void Toggle() { s_open = !s_open; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.modmenu";
        public override int Layer => 35; // above service windows / dialogue / modal; below the tooltip reader (40)
        public override bool IsActive() => s_open;

        private bool _priorFocus;
        private int _active;
        private int _builtActive;
        private bool _built;
        private Container _content; // wraps the active category's treeview; refilled on tab switch

        // Overlays tab: the live tree + the Add button + id→node map, so add/remove/reorder mutate the tree
        // in place (no destructive rebuild) and focus the affected node.
        private Container _overlaysTree;
        private UIElement _addButton;
        private readonly Dictionary<string, Container> _overlayNodes = new Dictionary<string, Container>();

        // Explicit tabs (the settings Root holds bindings/announcements/ui, which don't map 1:1 to tabs:
        // the UI tab composes the global announcement settings + the per-element-type overrides).
        private static readonly (string key, string label, string loc)[] Tabs =
        {
            ("input", "Input", "category.input"),
            ("ui", "UI", "category.ui"),
            ("overlays", "Overlays", "category.overlays"),
            ("audio", "Audio", "category.audio"),
        };

        public override void OnPush() { _priorFocus = FocusMode.Active; FocusMode.Set(true); _active = 0; _built = false; }
        public override void OnPop() { Clear(); _content = null; FocusMode.Set(_priorFocus); }

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
                // Global per-announcement-type settings in one collapsible node at the top (only the
                // [ShowInGlobalSettings] types are non-hidden).
                var ann = ModSettings.Root.Get<CategorySetting>("announcements");
                if (ann != null)
                {
                    var global = new TreeGroup(Loc("global.group", "Global"));
                    foreach (var s in ann.Children) BuildSettingNode(global, s);
                    if (global.Children.Count > 0) tree.Add(global);
                }

                // Each element type as its own root-level node, after Global — sorted alphabetically by
                // label (Global already sits on top, added above) so the list is easy to scan.
                var ui = ModSettings.Root.Get<CategorySetting>("ui");
                if (ui != null)
                    foreach (var s in ui.Children.OrderBy(c => c.Label, System.StringComparer.CurrentCultureIgnoreCase))
                        BuildSettingNode(tree, s);
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
                var audio = ModSettings.Root.Get<CategorySetting>("audio");
                if (audio != null)
                    foreach (var s in audio.Children) BuildSettingNode(tree, s);
            }
        }

        // One overlay = a collapsible node (Cursor + systems) with a LIVE "(standard)" tag, plus always-on
        // Make-standard + Remove actions. Returned (not added) so the caller can add or insert it.
        private Container BuildOverlayNode(CategorySetting oCat, string id)
        {
            var group = new TreeGroup("")
            {
                LabelProvider = () => WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayName(id)
                    + (WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.IsStandard(id)
                        ? " " + Loc("overlay.standard_tag", "(standard)") : "")
            };
            foreach (var c in oCat.Children) BuildSettingNode(group, c); // hidden "name" is skipped
            group.Add(new ProxyActionButton(Loc("overlay.make_standard", "Make standard"), null, () => MakeStandard(id)));
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

        private void MakeStandard(string id)
        {
            if (!WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.MakeDefault(id))
            {
                Tts.Speak(Loc("overlay.already_standard", "Already standard"));
                return;
            }
            Tts.Speak(Loc("overlay.made_standard", "Set as standard"));
            if (_overlaysTree != null && _overlayNodes.TryGetValue(id, out var node))
            {
                _overlaysTree.Remove(node);      // move to front; the live "(standard)" tags auto-update
                _overlaysTree.Insert(0, node);
                Navigation.Focus(node);
            }
        }

        // Map a setting to a navigable control; categories recurse into collapsible tree groups.
        private static void BuildSettingNode(Container parent, Setting s)
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
                case IntSetting i:
                    parent.Add(new ProxyIntSetting(i));
                    break;
                case ChoiceSetting c:
                    parent.Add(new ProxyChoiceDropdown(c.Label,
                        c.Choices.Select(ch => ch.Label).ToList(),
                        () => IndexOfChoice(c),
                        idx => { if (idx >= 0 && idx < c.Choices.Count) c.Set(c.Choices[idx].Id); }));
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
