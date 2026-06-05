using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A precise, free-moving cursor: hold the arrows to glide the world point continuously at a fixed
    /// ft/sec. It traces from the current point toward the intended one along the navmesh each frame
    /// (<see cref="ObstacleAnalyzer.TraceAlongNavmesh"/>) and stops at the first wall/ledge, so it can't
    /// leave walkable ground. Feedback is audio (wall tones / sonar), so it doesn't speak on move —
    /// describing the exact point is the Point-context job of <c>SpatialSystem</c>.
    /// </summary>
    internal sealed class ContinuousGlide : MovementMode
    {
        public override string Name => "Continuous glide";
        public override MovementSlot Slot => MovementSlot.Primary;
        public override AnnouncementContext Context => AnnouncementContext.Point;
        public override bool AnnouncesOnMove => false; // audio-driven, not per-frame speech

        private const float SpeedFeet = 15f; // ft/sec
        private static float Speed => SpeedFeet * Geo.MetresPerFoot;

        public override void OnEnter(Overlay overlay)
        {
            // Make sure the shared cursor is planted (so move-to-cursor has a point); the getter already
            // falls back to the player, so reading-then-writing pins it there on a cold start.
            overlay.Cursor.Position = overlay.Cursor.Position;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) return; // menu up / focus off → don't move

            float dx = 0f, dz = 0f;
            if (InputManager.Held("nav.up")) dz += 1f;     // +Z = north
            if (InputManager.Held("nav.down")) dz -= 1f;
            if (InputManager.Held("nav.right")) dx += 1f;  // +X = east
            if (InputManager.Held("nav.left")) dx -= 1f;
            if (dx == 0f && dz == 0f) return;

            var cur = overlay.Cursor.Position;
            var dir = new Vector3(dx, 0f, dz).normalized;
            var intended = cur + dir * (Speed * dt);
            overlay.Cursor.Position = ObstacleAnalyzer.TraceAlongNavmesh(cur, intended); // stops at walls/ledges
        }
    }
}
