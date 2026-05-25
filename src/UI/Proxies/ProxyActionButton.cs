using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>A generic button driven by delegates — for Apply / Close / etc.</summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyActionButton : UIElement
    {
        private readonly Func<string> _label;
        private readonly Func<bool> _enabled;
        private readonly Action _activate;

        public ProxyActionButton(string label, Func<bool> enabled, Action activate)
            : this(() => label, enabled, activate) { }

        // Live label — for buttons whose text changes (e.g. a wizard's Next → "Start").
        public ProxyActionButton(Func<string> label, Func<bool> enabled, Action activate)
        {
            _label = label;
            _enabled = enabled;
            _activate = activate;
        }

        private bool Enabled => _enabled == null || _enabled();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label != null ? _label() : ""));
            yield return new RoleAnnouncement("button");
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Activate"), _ => _activate?.Invoke());
        }
    }
}
