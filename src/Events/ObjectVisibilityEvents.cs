using WrathAccess.Exploration; // ScanItem

namespace WrathAccess.Events
{
    /// <summary>A scene object near the party was SHOWN by script — a puzzle's progress rune, a
    /// revealed prop (the Shield Maze torture-room buttons light a rune per press). The sighted
    /// player watches it appear; by ear this is the only feedback. Sourceless. Raised by
    /// <see cref="EventBusAdapter"/> (the game's IInGameHandler), batched over a short window.</summary>
    [EventSettings("Object appears", "exploration")]
    internal sealed class ObjectShownEvent : ModEvent
    {
        private readonly ScanItem _item;
        private readonly Message _message;
        /// <param name="message">A curated line (ObjectNames: "slot 1: yellow rune glows", or a group's
        /// "all runes glow"); null = the generic "{name} appears".</param>
        public ObjectShownEvent(ScanItem item, Message message = null) { _item = item; _message = message; }
        public override UnityEngine.Vector3 Position => _item.Position;
        public override Message GetMessage()
            => _message ?? Message.Localized("ui", "event.object_shown", new { name = _item.Name });
    }

    /// <summary>A scene object near the party was HIDDEN by script — a failed combination wiping
    /// the puzzle runes, a prop removed. Sourceless.</summary>
    [EventSettings("Object vanishes", "exploration")]
    internal sealed class ObjectHiddenEvent : ModEvent
    {
        private readonly ScanItem _item;
        private readonly Message _message;
        public ObjectHiddenEvent(ScanItem item, Message message = null) { _item = item; _message = message; }
        public override UnityEngine.Vector3 Position => _item.Position;
        public override Message GetMessage()
            => _message ?? Message.Localized("ui", "event.object_hidden", new { name = _item.Name });
    }
}
