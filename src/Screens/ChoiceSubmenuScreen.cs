using System;
using System.Collections.Generic;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A list of options to pick from (e.g. a dropdown's values), pushed as a CHILD SCREEN of whatever
    /// screen opened it (<see cref="Screen.PushChild"/>). As a child it's the focused screen while open and
    /// owns the keyboard; selecting an option or backing out removes it, and ScreenManager re-focuses the
    /// parent on its remembered control (the dropdown) automatically. Built eagerly in OnPush, so focus
    /// lands on the current option immediately — no lazy-build EnsureFocus dance. Reusable for any "open a
    /// list and pick one" interaction.
    /// </summary>
    public sealed class ChoiceSubmenuScreen : Screen
    {
        private readonly string _title;
        private readonly List<string> _options;
        private readonly int _current;
        private readonly Action<int> _onSelect;

        public ChoiceSubmenuScreen(string title, List<string> options, int current, Action<int> onSelect)
        {
            _title = title;
            _options = options;
            _current = current;
            _onSelect = onSelect;
            Wrap = true;
        }

        /// <summary>Open the submenu as a child of the current screen.</summary>
        public static void Open(string title, List<string> options, int current, Action<int> onSelect)
            => ScreenManager.Current?.PushChild(new ChoiceSubmenuScreen(title, options, current, onSelect));

        public override string Key => "overlay.choicesubmenu";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // never poll-pushed — only ever a child screen

        public override void OnPush() { Clear(); Build(); }
        public override void OnPop() { Clear(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back closes the submenu without changing the value.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        private void Build()
        {
            var list = new ListContainer();
            for (int i = 0; i < _options.Count; i++)
            {
                int idx = i;
                list.Add(new ProxyChoiceOption(_options[i], i == _current, () =>
                {
                    _onSelect?.Invoke(idx);
                    Close();
                }));
            }
            Add(list);
        }
    }
}
