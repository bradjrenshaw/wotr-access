using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root; // LocalizedTexts
using Kingmaker.UI.Common;       // ItemsFilter.SorterType
using Kingmaker.UI.MVVM._VM.Slots; // ItemsFilterVM
using WrathAccess.Screens;       // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The stash sort control — a combo over <see cref="ItemsFilter.SorterType"/> (Not sorted, Type/Price/
    /// Name/Date/Weight ↑·↓), mirroring the game's sort dropdown. Announces the current sort; activating
    /// opens a submenu of the localized options and applies the pick via
    /// <see cref="ItemsFilterVM.SetCurrentSorter"/>, which re-sorts the stash group.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(PositionAnnouncement))]
    public sealed class ProxyItemSorter : UIElement
    {
        private readonly ItemsFilterVM _filter;

        public ProxyItemSorter(ItemsFilterVM filter) { _filter = filter; }

        public override bool CanFocus => _filter != null;

        private static string SortName(ItemsFilter.SorterType t) => LocalizedTexts.Instance.ItemsFilter.GetText(t);

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "inventory.sort"));
            yield return new RoleAnnouncement("combo box");
            yield return new ValueAnnouncement(Message.Raw(SortName(_filter.CurrentSorter.Value)));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ =>
            {
                var values = (ItemsFilter.SorterType[])Enum.GetValues(typeof(ItemsFilter.SorterType));
                var labels = new List<string>(values.Length);
                int current = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    labels.Add(SortName(values[i]));
                    if (values[i] == _filter.CurrentSorter.Value) current = i;
                }
                ChoiceSubmenuScreen.Open(Message.Localized("ui", "inventory.sort").Resolve(), labels, current,
                    idx => { if (idx >= 0 && idx < values.Length) _filter.SetCurrentSorter(values[idx]); });
            });
        }
    }
}
