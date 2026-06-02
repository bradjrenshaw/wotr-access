using System;
using System.Collections.Generic;
using WrathAccess.Screens; // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A combo box over a fixed list of strings, opening the shared <see cref="ChoiceSubmenuScreen"/> —
    /// like the game's dropdowns. Generic (not bound to a game settings VM), driven by delegates: a
    /// live current-index getter and an on-select callback. Used e.g. for the feat tag filter.
    /// </summary>
    public sealed class ProxyChoiceDropdown : UIElement
    {
        // Shares the "combo box" settings category + announcement order (see ProxyDropdown).
        public override System.Type AnnouncementOrderType => typeof(ProxyDropdown);

        private readonly string _label;
        private readonly List<string> _options;
        private readonly Func<int> _current;
        private readonly Action<int> _onSelect;

        public ProxyChoiceDropdown(string label, List<string> options, Func<int> current, Action<int> onSelect)
        {
            _label = label;
            _options = options;
            _current = current;
            _onSelect = onSelect;
        }

        private int Current => _current != null ? _current() : -1;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label ?? ""));
            yield return new RoleAnnouncement("combo box");
            int i = Current;
            yield return new ValueAnnouncement(Message.Raw(
                (_options != null && i >= 0 && i < _options.Count) ? _options[i] : ""));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"),
                _ => ChoiceSubmenuScreen.Open(_label, _options, Current, _onSelect));
        }
    }
}
