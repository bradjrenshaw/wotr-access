using System.Collections.Generic;
using WrathAccess.Settings;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A mod <see cref="IntSetting"/> → slider. Left/Right step by the setting's <c>Step</c> (clamped to its
    /// range); the navigator announces the new value. Reads/writes the setting live. Used to render numeric
    /// mod settings (overlay speed, volumes, distances, master volume…) in the Ctrl+M menu.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyIntSetting : UIElement
    {
        private readonly IntSetting _setting;

        public ProxyIntSetting(IntSetting setting) { _setting = setting; }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_setting.Label));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Raw(_setting.Get().ToString()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.decrease"),
                _ => _setting.Set(_setting.Get() - _setting.Step));
            yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.increase"),
                _ => _setting.Set(_setting.Get() + _setting.Step));
        }
    }
}
