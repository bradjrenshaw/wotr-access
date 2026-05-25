using System;
using System.Collections.Generic;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A mod-owned overlay listing options to choose from (e.g. a dropdown's values),
    /// pushed on top of the current screen. "Mod-pushed" = its IsActive reads our own
    /// static state rather than a game window, so the resolver layers it like any other
    /// screen. Reusable for any "open a list and pick one" interaction.
    /// </summary>
    public sealed class ChoiceSubmenuScreen : Screen
    {
        private static string s_title;
        private static List<string> s_options;
        private static int s_current;
        private static Action<int> s_onSelect;

        /// <summary>Open the submenu with a list of options and a selection callback.</summary>
        public static void Open(string title, List<string> options, int current, Action<int> onSelect)
        {
            s_title = title;
            s_options = options;
            s_current = current;
            s_onSelect = onSelect;
        }

        public static void CloseSubmenu()
        {
            s_title = null;
            s_options = null;
            s_onSelect = null;
        }

        public ChoiceSubmenuScreen() { Wrap = true; }

        public override string Key => "overlay.choicesubmenu";
        public override string ScreenName => s_title;
        public override int Layer => 26; // above Settings (25)
        public override bool IsActive() => s_options != null;

        private List<string> _builtFor;

        public override void OnPush() { _builtFor = null; Build(); }
        public override void OnPop() { Clear(); _builtFor = null; }
        public override void OnUpdate() { if (s_options != _builtFor) Build(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back closes the submenu without changing the value.
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => CloseSubmenu());
        }

        private void Build()
        {
            Clear();
            _builtFor = s_options;
            if (s_options == null) return;

            var onSelect = s_onSelect; // capture; CloseSubmenu clears the statics
            var list = new ListContainer();
            for (int i = 0; i < s_options.Count; i++)
            {
                int idx = i;
                list.Add(new ProxyChoiceOption(s_options[i], i == s_current, () =>
                {
                    onSelect?.Invoke(idx);
                    CloseSubmenu();
                }));
            }
            Add(list);
        }
    }
}
