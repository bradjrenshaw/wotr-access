namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// Supplied to <see cref="Announcement.Render"/> and the composer so parts can resolve per-announcement
    /// settings (enabled, verbosity, …). <see cref="ResolveBool"/> consults the mod's UI settings: a global
    /// toggle per announcement type (<c>announcements.&lt;type&gt;</c>) — off → that part is skipped
    /// everywhere. Per-element-type overrides (keyed by <see cref="ElementKey"/>) layer on top later.
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

        public bool ResolveBool(string announcementKey, string settingKey, bool defaultValue)
        {
            // Global per-type toggle; only the toggleable types have a setting (others → default = shown).
            // (Per-element override by ElementKey will be checked first, here, in the next step.)
            var global = WrathAccess.Settings.ModSettings.GetSetting<WrathAccess.Settings.BoolSetting>(
                "announcements." + announcementKey);
            return global != null ? global.Get() : defaultValue;
        }
        public int ResolveInt(string announcementKey, string settingKey, int defaultValue) => defaultValue;
        public string ResolveString(string announcementKey, string settingKey, string defaultValue) => defaultValue;
    }
}
