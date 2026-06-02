using System.Collections.Generic;
using WrathAccess.Settings;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A per-element-type announcement override (a <see cref="NullableBoolSetting"/>). Shown as a checkbox
    /// of the RESOLVED value (the user doesn't see "inherit"); Enter writes an explicit on/off, and the
    /// secondary action (Backspace) resets it to inherit the global. Announces "overridden" when it carries
    /// an explicit value, so you can tell it apart from following the global.
    /// </summary>
    public sealed class ProxyOverrideToggle : UIElement
    {
        // Shares the "toggle" settings category + announcement order (see ProxyBoolToggle).
        public override System.Type AnnouncementOrderType => typeof(ProxyBoolToggle);

        private readonly NullableBoolSetting _setting;

        public ProxyOverrideToggle(NullableBoolSetting setting) { _setting = setting; }

        public override bool ReannounceOnActivate => true;
        public override bool ReannounceOnContext => true; // after Reset → re-read the (now inherited) value
        public override Kingmaker.UI.UISoundType? ActivateSound => Kingmaker.UI.UISoundType.SettingsSwitchToggle;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_setting.Label));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(Message.Join(", ",
                _setting.Resolved ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"),
                _setting.IsOverridden ? Message.Localized("ui", "value.overridden") : null));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"), _ => _setting.ToggleExplicit());
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.reset"), _ => _setting.Reset());
        }
    }
}
