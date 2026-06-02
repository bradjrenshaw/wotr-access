using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings;
using Kingmaker.UI.MVVM._VM.Settings.Menu;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>A settings tab (Game / Controls / Graphics / Sound). Activate switches to it.</summary>
    public sealed class ProxySettingsTab : UIElement
    {
        // Shares the "tab" settings category + announcement order (see ProxyTab).
        public override System.Type AnnouncementOrderType => typeof(ProxyTab);

        private readonly SettingsMenuEntityVM _tab;
        private readonly SettingsVM _settings;

        public ProxySettingsTab(SettingsMenuEntityVM tab, SettingsVM settings)
        {
            _tab = tab;
            _settings = settings;
        }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool IsSelected => _settings != null && ReferenceEquals(_settings.SelectedMenuEntity.Value, _tab);

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_tab?.Title ?? ""));
            yield return new RoleAnnouncement("tab");
            yield return new SelectedAnnouncement(IsSelected); // speaks "selected" only on the current tab
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // SetSelectedFromView(true) is exactly what the tab button calls on click:
            // SetSelected → IsSelected (group updates SelectedEntity) → DoSelectMe → SetSettingsList.
            // So we replicate the game's own click flow rather than poking VM state ourselves.
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ => _tab?.SetSelectedFromView(true));
        }
    }
}
