namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// Supplied to <see cref="Announcement.Render"/> and the composer so parts can
    /// resolve per-announcement settings (enabled, verbosity, …). The config backend
    /// isn't built yet, so Resolve* return defaults — but elements already declare
    /// keyed announcements, so wiring a real per-element → global settings cascade
    /// here later makes them configurable with no element changes.
    /// </summary>
    public sealed class AnnouncementContext
    {
        public UIElement Element { get; }
        public string ElementKey { get; }

        public AnnouncementContext(UIElement element)
        {
            Element = element;
            ElementKey = element != null ? element.AnnouncementOrderType.Name : null;
        }

        public bool ResolveBool(string announcementKey, string settingKey, bool defaultValue) => defaultValue;
        public int ResolveInt(string announcementKey, string settingKey, int defaultValue) => defaultValue;
        public string ResolveString(string announcementKey, string settingKey, string defaultValue) => defaultValue;
    }
}
