using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A precise, free-moving cursor: hold the arrows to glide the world point continuously at a
    /// configurable ft/sec. It traces from the current point toward the intended one along the navmesh
    /// each frame (<see cref="ObstacleAnalyzer.TraceAlongNavmesh"/>) and stops at the first wall/ledge, so
    /// it can't leave walkable ground. Feedback is audio (wall tones / sonar), so it doesn't speak on
    /// move — describing the exact point is the Point-context job of <c>SpatialSystem</c>. Speed reads live
    /// from the cursor slot's settings.
    /// </summary>
    internal sealed class ContinuousGlide : MovementMode
    {
        private readonly MovementSlot _slot;
        private readonly CategorySetting _settings; // cursor.<slot> — holds "speed"

        public ContinuousGlide(MovementSlot slot, CategorySetting settings)
        {
            _slot = slot;
            _settings = settings;
        }

        public override string Name => "Continuous glide";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Point;
        public override bool AnnouncesOnMove => false; // audio-driven, not per-frame speech

        private float Speed => (_settings?.Get<IntSetting>("speed")?.Get() ?? 15) * Geo.MetresPerFoot;

        // Opt-in wall sliding (Exploration → Cursor), read live — a GLOBAL cursor behaviour, not a
        // per-slot/per-overlay knob, so it lives on the shared defaults.cursor category.
        private static bool WallSlide =>
            WrathAccess.Settings.ModSettings.GetSetting<WrathAccess.Settings.BoolSetting>(
                "defaults.cursor.wall_slide")?.Get() ?? false;

        public override void OnEnter(Overlay overlay)
        {
            // Make sure the shared cursor is planted (so move-to-cursor has a point); the getter already
            // falls back to the player, so reading-then-writing pins it there on a cold start.
            overlay.Cursor.Position = overlay.Cursor.Position;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) return;            // menu up / focus off → don't move

            CursorKeys.HeldVector(_slot, out int ix, out int iz);
            float dx = ix, dz = iz;
            if (dx == 0f && dz == 0f) return;
            ListenerFrame.InputToWorld(ref dx, ref dz); // W = forward of the facing (default north)

            var cur = overlay.Cursor.Position;
            var dir = new Vector3(dx, 0f, dz).normalized;
            float step = Speed * dt;
            var intended = cur + dir * step;
            // Wall slide (opt-in): a blocked glide slides along the wall's tangent — the same trace the
            // game's direct-control movement uses — funnelling through doorways instead of dead-stopping.
            var traced = WallSlide
                ? ObstacleAnalyzer.TraceAlongNavmeshWithWallSlide(cur, intended)
                : ObstacleAnalyzer.TraceAlongNavmesh(cur, intended); // stops at walls/ledges

            // DEFAULT-mode diagonal fallback: holding two directions is explicit intent for BOTH, but
            // the combined ray dies at zero distance against a wall on either axis — discarding the
            // free half (hold south-east beside an east wall: nothing moves, though south is open).
            // When the diagonal makes no progress, walk the free axis instead. Single-direction input
            // into a wall still dead-stops — the deliberate "bumped into it" cue.
            if (!WallSlide && dx != 0f && dz != 0f && (traced - cur).sqrMagnitude < 1e-6f)
            {
                var tx = ObstacleAnalyzer.TraceAlongNavmesh(cur, cur + new Vector3(dir.x, 0f, 0f).normalized * step);
                var tz = ObstacleAnalyzer.TraceAlongNavmesh(cur, cur + new Vector3(0f, 0f, dir.z).normalized * step);
                traced = (tx - cur).sqrMagnitude >= (tz - cur).sqrMagnitude ? tx : tz;
            }
            // Re-project onto the walkable surface: the trace's unobstructed result keeps the INPUT Y
            // (the navmesh linecast never re-snaps height), so gliding up a ramp left the cursor's Y
            // fossilized at wherever it was last planted — path-dependent heights on one slope, and a
            // stale feed to the slope indicator. Seeding the sample with the current Y keeps genuine
            // multi-level geometry honest (nearest tier wins; ascend/descend still switch tiers).
            var s = NavmeshProbe.Sample(traced.x, traced.z, traced.y);
            if (s.OnNavmesh) traced.y = s.Point.y;
            overlay.Cursor.Position = traced;
        }
    }
}
