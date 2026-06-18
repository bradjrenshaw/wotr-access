using System.Collections.Generic;
using WrathAccess.Settings;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A mod <see cref="NullableIntSetting"/> → slider that follows the default config until overridden.
    /// Announces the RESOLVED value plus whether it's overridden or inherited; Left/Right step from the
    /// resolved value and write an explicit override, and the secondary action (Backspace) resets it back
    /// to inherit. Mirrors <see cref="ProxyIntSetting"/> + <see cref="ProxyOverrideToggle"/>.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyNullableIntSetting : UIElement
    {
        private readonly NullableIntSetting _setting;

        public ProxyNullableIntSetting(NullableIntSetting setting) { _setting = setting; }

        public override bool ReannounceOnContext => true; // after Reset → re-read the (now inherited) value

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_setting.Label));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Join(", ",
                Message.Raw(_setting.Resolved.ToString()),
                Message.Localized("ui", _setting.IsOverridden ? "value.overridden" : "value.inherited")));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.decrease"),
                _ => _setting.SetExplicit(_setting.Resolved - _setting.Step));
            yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.increase"),
                _ => _setting.SetExplicit(_setting.Resolved + _setting.Step));
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.reset"),
                _ => _setting.Reset());
        }
    }
}
