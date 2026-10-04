namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// One spoken fragment a <see cref="OverlaySystem"/> contributes about the cursor's surroundings,
    /// tagged with the <see cref="AnnouncementContext"/> it describes (tile vs point). The overlay's
    /// announce pipeline keeps the fragments whose context matches what's being looked at, orders them
    /// by <see cref="Order"/> (lower first: where you are, then what an aimed ability would hit, then what
    /// it costs to get there, then what is there), drops exact repeats, and composes one utterance. (Audio — sonar, wall tones,
    /// fog/object cues — is system <c>Tick</c> work, not an announcement.) Mirrors the typed-announcement
    /// pattern from the UI layer.
    /// </summary>
    internal sealed class OverlayAnnouncement
    {
        public const int OrderPosition = 0;   // bearing/distance, the tile readout
        public const int OrderEffect = 10;    // what an aimed ability would hit
        public const int OrderPath = 20;      // path cost (turn-based)
        public const int OrderContents = 30;  // the thing under the cursor

        public readonly AnnouncementContext Context;
        public readonly Message Text;
        public readonly int Order;

        public OverlayAnnouncement(AnnouncementContext context, Message text, int order = 50)
        {
            Context = context;
            Text = text;
            Order = order;
        }
    }
}
