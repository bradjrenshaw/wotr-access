using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Placeholder for control types we haven't built proxies for yet (slider,
    /// dropdown, key binding). Keeps the tab navigable and tells the user the
    /// setting exists but isn't accessible yet. Replaced as we add those proxies.
    /// </summary>
    public sealed class ProxyUnsupportedSetting : UIElement
    {
        private readonly string _label;
        public ProxyUnsupportedSetting(string label) { _label = label; }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("setting");
            yield return new ValueAnnouncement(Message.Raw("not accessible yet"));
        }
    }
}
