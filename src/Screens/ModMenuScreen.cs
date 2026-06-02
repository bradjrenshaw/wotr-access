using System.Collections.Generic;
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

        private static readonly string[] CatKeys = { "input", "ui" };
        private static readonly string[] CatLabels = { "Input", "UI" };

        private bool _priorFocus;
        private int _active;
        private int _builtActive;
        private bool _built;
        private Container _content; // wraps the active category's treeview; refilled on tab switch

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
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => CloseMenu());
        }

        // Localized menu string ("settings" table) with the English fallback.
        private static string Loc(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);

        private void Build()
        {
            _built = true;
            Clear();

            var tabs = new ListContainer(Loc("menu.categories", "Categories"));
            for (int i = 0; i < CatKeys.Length; i++)
            {
                int idx = i;
                tabs.Add(new ProxyTab(Loc("category." + CatKeys[i], CatLabels[i]), () => _active == idx, () => _active = idx));
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
            BuildCategory(tree, CatKeys[_active]);
            _content.Add(tree);
        }

        private void BuildCategory(TreeGroup tree, string key)
        {
            if (key == "input")
            {
                var bindings = ModSettings.Root.Get<CategorySetting>("bindings");
                if (bindings != null)
                    foreach (var s in bindings.Children)
                        if (s is BindingSetting bs)
                            tree.Add(new ProxyModBinding(bs.Action));
            }
            else // "ui" — announcement settings (built next)
            {
                tree.Add(new TextElement(() => Loc("ui.placeholder", "Announcement settings coming soon.")));
            }
        }
    }
}
