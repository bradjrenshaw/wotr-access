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

        // Opt-in first-direction priority steering (user-designed): diagonals move normally in the
        // OPEN, but on the first wall contact the FIRST-held key becomes the goal — the second key
        // wall-follows, and the instant the goal direction opens the cursor turns INTO the gap
        // (instead of diagonally overshooting it). Releasing/changing the held keys resets to free.
        private static bool DirectionPriority =>
            WrathAccess.Settings.ModSettings.GetSetting<WrathAccess.Settings.BoolSetting>(
                "defaults.cursor.direction_priority")?.Get() ?? false;

        // EXPERIMENTAL collision naming (on by default): a pure readout, so it lives with the other
        // cursor behaviours on defaults.cursor, not under Enhancements.
        private static bool CollisionNames =>
            WrathAccess.Settings.ModSettings.GetSetting<WrathAccess.Settings.BoolSetting>(
                "defaults.cursor.collision_names")?.Get() ?? true;

        // Priority-steering state, per slot (this mode instance IS per-slot): which INPUT axis was
        // held first (0 = x/east-west, 1 = z/north-south), whether we're wall-following, and last
        // frame's held axes for change detection.
        private int _prioAxis = -1;
        private bool _wallFollow;
        private bool _prevHx, _prevHz;

        // Collision naming (experimental enhancement): remember when the last held frame dead-stopped
        // and in which world direction, so releasing the keys can name what was in the way.
        private bool _blocked;
        private Vector3 _blockedDir;

        public override void OnEnter(Overlay overlay)
        {
            // Make sure the shared cursor is planted (so move-to-cursor has a point); the getter already
            // falls back to the player, so reading-then-writing pins it there on a cold start.
            overlay.Cursor.Position = overlay.Cursor.Position;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) { _blocked = false; return; } // menu up / focus off → don't move

            CursorKeys.HeldVector(_slot, out int ix, out int iz);
            if (ix == 0 && iz == 0)
            {
                _prioAxis = -1; _wallFollow = false; _prevHx = _prevHz = false;
                // Keys just released against something → name what was in the way (experimental).
                if (_blocked)
                {
                    _blocked = false;
                    if (CollisionNames)
                        CollisionNamer.Announce(overlay.Cursor.Position, _blockedDir);
                }
                return;
            }
            TrackPriority(ix != 0, iz != 0);

            bool moved = Move(ix, iz, Speed * dt, overlay);
            _blocked = !moved;
            if (_blocked)
            {
                float wx = ix, wz = iz;
                ListenerFrame.InputToWorld(ref wx, ref wz);
                _blockedDir = new Vector3(wx, 0f, wz);
            }
        }

        // One glide frame's movement resolution; true when the cursor made any progress.
        private bool Move(int ix, int iz, float step, Overlay overlay)
        {
            // First-direction priority steering (opt-in), only meaningful with BOTH axes held:
            // diagonal while the way is open; first wall contact arms wall-following — from then on
            // the priority axis is tried EVERY frame (turning into the gap the moment it opens),
            // else the second axis follows the wall.
            if (DirectionPriority && ix != 0 && iz != 0)
            {
                if (!_wallFollow)
                {
                    if (TryMove(ix, iz, step, overlay, slide: false)) return true;
                    _wallFollow = true;
                }
                bool prioX = _prioAxis != 1; // unset/x → x leads (both-same-frame picks x, arbitrary)
                if (TryMove(prioX ? ix : 0, prioX ? 0 : iz, step, overlay, slide: false)) return true;
                return TryMove(prioX ? 0 : ix, prioX ? iz : 0, step, overlay, slide: false);
            }

            // Normal path: combined vector, wall-slide honoured; blocked diagonals fall back to the
            // free axis (holding two directions is explicit intent for both — don't discard the open
            // half because the other is walled). Single-direction into a wall still dead-stops.
            if (TryMove(ix, iz, step, overlay, slide: WallSlide)) return true;
            if (ix != 0 && iz != 0 && !WallSlide)
            {
                return TryMove(ix, 0, step, overlay, slide: false)
                    || TryMove(0, iz, step, overlay, slide: false);
            }
            return false;
        }

        // Which INPUT axis was held first (the priority axis for the steering mode). Promotes the
        // survivor when the priority key is released; resets when everything is released.
        private void TrackPriority(bool hx, bool hz)
        {
            if (_prioAxis == 0 && !hx) _prioAxis = hz ? 1 : -1;
            else if (_prioAxis == 1 && !hz) _prioAxis = hx ? 0 : -1;
            else if (_prioAxis == -1) _prioAxis = hx && !hz ? 0 : (hz && !hx ? 1 : (hx ? 0 : -1));
            // Any change in the held set drops wall-following back to free movement.
            if (hx != _prevHx || hz != _prevHz) _wallFollow = false;
            _prevHx = hx; _prevHz = hz;
        }

        /// <summary>Attempt one glide step along the given INPUT vector (rotated by the listener
        /// facing). Moves the cursor and returns true when the trace made real progress.</summary>
        private bool TryMove(float inDx, float inDz, float step, Overlay overlay, bool slide)
        {
            if (inDx == 0f && inDz == 0f) return false;
            ListenerFrame.InputToWorld(ref inDx, ref inDz); // W = forward of the facing (default north)
            var cur = overlay.Cursor.Position;
            var dir = new Vector3(inDx, 0f, inDz).normalized;
            var intended = cur + dir * step;
            // Wall slide: blocked motion slides along the wall's tangent — the same trace the game's
            // direct-control movement uses — funnelling through doorways instead of dead-stopping.
            var traced = slide
                ? ObstacleAnalyzer.TraceAlongNavmeshWithWallSlide(cur, intended)
                : ObstacleAnalyzer.TraceAlongNavmesh(cur, intended); // stops at walls/ledges
            if ((traced - cur).sqrMagnitude < 1e-6f) return false;
            // Re-project onto the walkable surface: the trace's unobstructed result keeps the INPUT Y
            // (the navmesh linecast never re-snaps height), so gliding up a ramp left the cursor's Y
            // fossilized at wherever it was last planted — path-dependent heights on one slope, and a
            // stale feed to the slope indicator. Seeding the sample with the current Y keeps genuine
            // multi-level geometry honest (nearest tier wins; ascend/descend still switch tiers).
            var s = NavmeshProbe.Sample(traced.x, traced.z, traced.y);
            if (s.OnNavmesh) traced.y = s.Point.y;
            overlay.Cursor.Position = traced;
            return true;
        }
    }
}
