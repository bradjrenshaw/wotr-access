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

        // Explicit tabs (the settings Root holds bindings/announcements/ui, which don't map 1:1 to tabs:
        // the UI tab composes the global announcement settings + the per-element-type overrides).
        private static readonly (string key, string label, string loc)[] Tabs =
        {
            ("input", "Input", "category.input"),
            ("ui", "UI", "category.ui"),
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
            Tts.Speak(Loc("menu.title", "Mod menu")); // once, on open (Build runs the frame after focus)
            Navigation.AnnounceCurrent();
        }

        // Refill ONLY the content wrapper (tabs untouched) so focus on the tab list survives a switch — the
        // new tree (and its label) replaces the old; the user Tabs into it.
        private void RebuildContent()
        {
            _builtActive = _active;
            if (_content == null) return;
            _content.Clear();
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
            }
        }
    }
}
