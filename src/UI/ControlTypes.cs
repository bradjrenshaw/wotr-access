using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// The control-type registry: each entry is a <see cref="ControlType"/> VALUE — its settings key, the
    /// speak order of its announcement kinds, and the parts common to every control of the type (the
    /// localized role word). This replaces the legacy per-class identity ([AnnouncementOrder] attributes +
    /// [ElementSettingsKey] collapsing): a node factory just sets the type and gets the role word, the
    /// ordering, and the user's per-type announcement settings for free. Keys deliberately match the
    /// legacy collapsed keys where the concept already existed ("toggle", "slider"), so both systems share
    /// one settings identity during the migration.
    /// </summary>
    public static class ControlTypes
    {
        private static readonly string[] StandardOrder =
        {
            AnnouncementKinds.Label,
            AnnouncementKinds.Role,
            AnnouncementKinds.Value,
            AnnouncementKinds.Selected,
            AnnouncementKinds.Enabled,
            AnnouncementKinds.Tooltip,
            AnnouncementKinds.Position,
        };

        private static NodeAnnouncement[] RoleWord(string word)
            => new[] { new NodeAnnouncement(() => Loc.T("role." + word), kind: AnnouncementKinds.Role) };

        public static readonly ControlType Button = new ControlType
        {
            Key = "button",
            Order = StandardOrder,
            Common = () => RoleWord("button"),
        };

        public static readonly ControlType Toggle = new ControlType
        {
            Key = "toggle",
            Order = StandardOrder,
            Common = () => RoleWord("toggle"),
        };

        public static readonly ControlType Slider = new ControlType
        {
            Key = "slider",
            Order = StandardOrder,
            Common = () => RoleWord("slider"),
        };

        /// <summary>A read-only text line — no role word; typed so its parts are still user-configurable.</summary>
        public static readonly ControlType Text = new ControlType
        {
            Key = "text",
            Order = StandardOrder,
        };

        /// <summary>Every registered type, for settings registration. New types are added here.</summary>
        public static readonly ControlType[] All = { Button, Toggle, Slider, Text };
    }
}
