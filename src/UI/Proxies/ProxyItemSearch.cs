using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Slots; // ItemsFilterVM
using WrathAccess.Screens;         // ModTextEntryScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The stash search box. Announces the current search text (or blank); activating opens the mod text
    /// entry and writes the result through <see cref="ItemsFilterSearchVM.SetSearchString"/>, which filters
    /// the stash group by name. We drive the model directly rather than the game's on-screen input field
    /// (which lives on the view we bypass).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(PositionAnnouncement))]
    public sealed class ProxyItemSearch : UIElement
    {
        private readonly ItemsFilterSearchVM _search;
        private readonly Func<string> _current;

        public ProxyItemSearch(ItemsFilterSearchVM search, Func<string> current)
        {
            _search = search;
            _current = current;
        }

        public override bool CanFocus => _search != null;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "inventory.search"));
            yield return new RoleAnnouncement("edit");
            var v = _current?.Invoke();
            yield return new ValueAnnouncement(string.IsNullOrEmpty(v)
                ? Message.Localized("ui", "value.blank") : Message.Raw(v));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.edit"), _ =>
                ModTextEntryScreen.Open(Message.Localized("ui", "inventory.search").Resolve(),
                    _current?.Invoke() ?? "", s => _search.SetSearchString(s ?? "")));
        }
    }
}
