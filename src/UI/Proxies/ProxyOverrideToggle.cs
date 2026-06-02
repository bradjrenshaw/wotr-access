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
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyOverrideToggle : UIElement
    {
        private readonly NullableBoolSetting _setting;

        public ProxyOverrideToggle(NullableBoolSetting setting) { _setting = setting; }

        public override bool ReannounceOnActivate => true;
        public override bool ReannounceOnContext => true; // after Reset → re-read the (now inherited) value
        public override Kingmaker.UI.UISoundType? ActivateSound => Kingmaker.UI.UISoundType.SettingsSwitchToggle;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_setting.Label));
            yield return new RoleAnnouncement("checkbox");
            var value = (_setting.Resolved ? "checked" : "unchecked") + (_setting.IsOverridden ? ", overridden" : "");
            yield return new ValueAnnouncement(Message.Raw(value));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Toggle"), _ => _setting.ToggleExplicit());
            yield return new ElementAction(ActionIds.Context, Message.Raw("Reset to default"), _ => _setting.Reset());
        }
    }
}
