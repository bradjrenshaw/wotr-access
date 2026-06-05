using System.Collections.Generic;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The continuous-space lens: describes the exact point under the cursor — direction, distance, and
    /// vertical offset from the player (<see cref="Geo.Relative"/>). Point context, so it pairs with a
    /// free-gliding cursor; the tiled readout is <see cref="GridSystem"/>'s job.
    /// </summary>
    internal sealed class SpatialSystem : OverlaySystem
    {
        public override string Name => "Spatial";

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (ctx.Want != AnnouncementContext.Point) yield break;
            yield return new OverlayAnnouncement(AnnouncementContext.Point,
                Message.Raw(Geo.Relative(ctx.Reference, ctx.Cursor)));
        }
    }
}
