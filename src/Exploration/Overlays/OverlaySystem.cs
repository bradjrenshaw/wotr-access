using System.Collections.Generic;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A pure provider attached to an <see cref="Overlay"/> — it queries the world relative to the cursor
    /// and exposes <see cref="OverlayAnnouncement"/>s and/or makes sound in <see cref="Tick"/>; it NEVER
    /// moves the cursor or owns movement keys (that's <see cref="MovementMode"/>), which is what lets a
    /// tiled-space system and a continuous-space system coexist on one overlay. Exactly one of each
    /// concrete type per overlay, so siblings can be looked up by type.
    /// </summary>
    internal abstract class OverlaySystem
    {
        public abstract string Name { get; }

        /// <summary>The overlay became / stopped being active. Acquire/release resources (e.g. audio).</summary>
        public virtual void OnEnter(Overlay overlay) { }
        public virtual void OnExit(Overlay overlay) { }

        /// <summary>Per-frame work while an overlay is selected (continuous audio, event cues). The system
        /// self-gates on <see cref="OverlayManager.Active"/> so it idles/mutes when a menu's up.</summary>
        public virtual void Tick(float dt, Overlay overlay) { }

        /// <summary>The announcements this system contributes (each tagged with its
        /// <see cref="AnnouncementContext"/>); empty if none. The overlay filters by the requested
        /// context.</summary>
        public virtual IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            yield break;
        }
    }
}
