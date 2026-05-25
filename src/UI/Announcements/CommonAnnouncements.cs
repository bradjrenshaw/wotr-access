namespace WrathAccess.UI.Announcements
{
    /// <summary>The element's name/label.</summary>
    public sealed class LabelAnnouncement : Announcement
    {
        private readonly Message _text;
        public LabelAnnouncement(Message text) { _text = text; }
        public override string Key => "label";
        public override Message Render(AnnouncementContext ctx) => _text ?? Message.Empty;
    }

    /// <summary>The control type: "button", "toggle", "slider", "list"…</summary>
    public sealed class RoleAnnouncement : Announcement
    {
        private readonly string _role;
        public RoleAnnouncement(string role) { _role = role; }
        public override string Key => "role";
        public override Message Render(AnnouncementContext ctx) => Message.Raw(_role);
    }

    /// <summary>
    /// Interactability. Speaks only when the control can't be used ("disabled") —
    /// stays silent (empty, skipped) when enabled. Separate from value so the two
    /// don't share one clunky "status" slot.
    /// </summary>
    public sealed class EnabledAnnouncement : Announcement
    {
        private readonly bool _enabled;
        public EnabledAnnouncement(bool enabled) { _enabled = enabled; }
        public override string Key => "enabled";
        public override Message Render(AnnouncementContext ctx) =>
            _enabled ? Message.Empty : Message.Raw("disabled");
    }

    /// <summary>Selection state. Speaks "selected" only when selected (else silent) — like Enabled.</summary>
    public sealed class SelectedAnnouncement : Announcement
    {
        private readonly bool _selected;
        public SelectedAnnouncement(bool selected) { _selected = selected; }
        public override string Key => "selected";
        public override Message Render(AnnouncementContext ctx) =>
            _selected ? Message.Raw("selected") : Message.Empty;
    }

    /// <summary>The control's current value/state: "checked"/"unchecked", a slider amount, a dropdown option.</summary>
    public sealed class ValueAnnouncement : Announcement
    {
        private readonly Message _text;
        public ValueAnnouncement(Message text) { _text = text; }
        public override string Key => "value";
        public override Message Render(AnnouncementContext ctx) => _text ?? Message.Empty;
    }

    /// <summary>
    /// A "simple" tooltip — the header/body description text of a control (e.g. a settings
    /// entity's TooltipDescription). Read just before position; empty when there's none, so it
    /// self-skips. (The rich, brick/glossary-link tooltips are a separate later feature.)
    /// </summary>
    public sealed class TooltipAnnouncement : Announcement
    {
        private readonly Message _text;
        public TooltipAnnouncement(Message text) { _text = text; }
        public override string Key => "tooltip";
        public override Message Render(AnnouncementContext ctx) => _text ?? Message.Empty;
    }

    /// <summary>Position within the parent container, e.g. "2 of 8". Injected by GetFocusMessage.</summary>
    public sealed class PositionAnnouncement : Announcement
    {
        private readonly Message _pos;
        public PositionAnnouncement(Message pos) { _pos = pos; }
        public override string Key => "position";
        public override Message Render(AnnouncementContext ctx) => _pos ?? Message.Empty;
    }

    /// <summary>Item count for a container, e.g. "8 items".</summary>
    public sealed class CountAnnouncement : Announcement
    {
        private readonly int _count;
        public CountAnnouncement(int count) { _count = count; }
        public override string Key => "count";
        public override Message Render(AnnouncementContext ctx) => Message.Raw(_count + " items");
    }
}
