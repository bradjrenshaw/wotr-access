using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// What a system is handed when asked to announce: the current cursor point, the player reference
    /// (origin for bearing/distance), the <see cref="AnnouncementContext"/> being requested (so a system
    /// can skip contexts it doesn't serve), the tile edge in world metres the cursor is currently stepping
    /// by (for tile-context readouts), and the owning overlay (so one system can read a sibling —
    /// one-per-type makes that deterministic via <see cref="Overlay.Get{T}"/>).
    /// </summary>
    internal sealed class OverlayContext
    {
        public Overlay Overlay { get; }
        public Vector3 Cursor { get; }
        public Vector3 Reference { get; }
        public AnnouncementContext Want { get; }
        public ReadoutTrigger Trigger { get; }
        public float Cell { get; }

        public OverlayContext(Overlay overlay, Vector3 cursor, Vector3 reference, AnnouncementContext want,
            ReadoutTrigger trigger = ReadoutTrigger.Demand, float cell = 5f * Geo.MetresPerFoot)
        {
            Cell = cell;
            Overlay = overlay;
            Cursor = cursor;
            Reference = reference;
            Want = want;
            Trigger = trigger;
        }

        public T System<T>() where T : OverlaySystem => Overlay.Get<T>();
    }
}
