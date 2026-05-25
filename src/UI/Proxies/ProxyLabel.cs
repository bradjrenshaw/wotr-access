using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Read-only static text (e.g. a dialog's message body). Focusable so it's a tab-stop
    /// you can land on to re-read, but advertises no actions. Optional role.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyLabel : UIElement
    {
        private readonly string _text;
        private readonly string _role;

        public ProxyLabel(string text, string role = null) { _text = text; _role = role; }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_text ?? ""));
            if (!string.IsNullOrEmpty(_role)) yield return new RoleAnnouncement(_role);
        }
    }
}
