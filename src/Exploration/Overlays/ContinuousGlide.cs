using UnityEngine;
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A precise, free-moving cursor: hold the arrows to glide the point continuously at a configurable
    /// speed (ft/sec in an area, miles/sec on the map). Each frame it asks the cursor's
    /// <see cref="CursorSpace"/> to trace from the current point toward the intended one — along the navmesh
    /// in an area (stopping at the first wall/ledge, so it can't leave walkable ground), freely on the flat
    /// map. Feedback is audio (wall tones / sonar), so it doesn't speak on move — the idle settle readout
    /// describes where it stops. Speed reads live from the cursor slot's settings.
    /// </summary>
    internal sealed class ContinuousGlide : MovementMode
    {
        private readonly MovementSlot _slot;
        private readonly CategorySetting _settings; // the slot's category (mode + per-mode subtrees)
        private readonly CategorySetting _context;  // the exploration context (the behaviour flags)

        public ContinuousGlide(MovementSlot slot, CategorySetting settings, CategorySetting context)
        {
            _slot = slot;
            _settings = settings;
            _context = context;
        }

        public override string Name => "Continuous glide";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Point;
        public override bool AnnouncesOnMove => false; // audio-driven, not per-frame speech

        private float Speed(Cursor cursor)
            => CursorSettings.ContinuousSpeed(_settings, cursor.Space.DefaultSpeed) * cursor.Space.Unit;

        // Opt-in wall sliding (Cursor → Exploration), read live from the context this mode was built
        // against (the overlay's custom copy or the shared defaults).
        private bool WallSlide => CursorSettings.Flag(_context, "wall_slide");

        // Opt-in first-direction priority steering (user-designed): diagonals move normally in the
        // OPEN, but on the first wall contact the FIRST-held key becomes the goal — the second key
        // wall-follows, and the instant the goal direction opens the cursor turns INTO the gap
        // (instead of diagonally overshooting it). Releasing/changing the held keys resets to free.
        private bool DirectionPriority => CursorSettings.Flag(_context, "direction_priority");

        // EXPERIMENTAL collision naming (opt-in): a pure readout, so it lives with the other
        // cursor behaviours on defaults.cursor, not under Enhancements.
        private bool CollisionNames => CursorSettings.Flag(_context, "collision_names");

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

        public override void OnEnter(Cursor cursor)
        {
            // Make sure the shared cursor is planted (so move-to-cursor has a point); the getter already
            // falls back to the player, so reading-then-writing pins it there on a cold start.
            cursor.Position = cursor.Position;
        }

        public override void Idle(Cursor cursor)
        {
            _blocked = false; _prioAxis = -1; _wallFollow = false; _prevHx = _prevHz = false;
        }

        public override void Tick(float dt, Cursor cursor)
        {
            if (!OverlayManager.Active) { _blocked = false; return; } // menu up / focus off → don't move

            cursor.HeldVector(_slot, out int ix, out int iz);
            if (ix == 0 && iz == 0)
            {
                _prioAxis = -1; _wallFollow = false; _prevHx = _prevHz = false;
                // Keys just released against something → name what was in the way (experimental).
                if (_blocked)
                {
                    _blocked = false;
                    if (CollisionNames)
                        CollisionNamer.Announce(cursor.Position, _blockedDir);
                }
                return;
            }
            TrackPriority(ix != 0, iz != 0);

            bool moved = Move(ix, iz, Speed(cursor) * dt, cursor);
            _blocked = !moved;
            if (_blocked)
            {
                float wx = ix, wz = iz;
                cursor.Space.InputToWorld(ref wx, ref wz);
                _blockedDir = new Vector3(wx, 0f, wz);
            }
        }

        // One glide frame's movement resolution; true when the cursor made any progress.
        private bool Move(int ix, int iz, float step, Cursor cursor)
        {
            // First-direction priority steering (opt-in), only meaningful with BOTH axes held:
            // diagonal while the way is open; first wall contact arms wall-following — from then on
            // the priority axis is tried EVERY frame (turning into the gap the moment it opens),
            // else the second axis follows the wall.
            if (DirectionPriority && ix != 0 && iz != 0)
            {
                if (!_wallFollow)
                {
                    if (TryMove(ix, iz, step, cursor, slide: false)) return true;
                    _wallFollow = true;
                }
                bool prioX = _prioAxis != 1; // unset/x → x leads (both-same-frame picks x, arbitrary)
                if (TryMove(prioX ? ix : 0, prioX ? 0 : iz, step, cursor, slide: false)) return true;
                return TryMove(prioX ? 0 : ix, prioX ? iz : 0, step, cursor, slide: false);
            }

            // Normal path: combined vector, wall-slide honoured; blocked diagonals fall back to the
            // free axis (holding two directions is explicit intent for both — don't discard the open
            // half because the other is walled). Single-direction into a wall still dead-stops.
            if (TryMove(ix, iz, step, cursor, slide: WallSlide)) return true;
            if (ix != 0 && iz != 0 && !WallSlide)
            {
                return TryMove(ix, 0, step, cursor, slide: false)
                    || TryMove(0, iz, step, cursor, slide: false);
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

        /// <summary>Attempt one glide step along the given INPUT vector (rotated into the space's world
        /// axes). Moves the cursor and returns true when the trace made real progress.</summary>
        private bool TryMove(float inDx, float inDz, float step, Cursor cursor, bool slide)
        {
            if (inDx == 0f && inDz == 0f) return false;
            cursor.Space.InputToWorld(ref inDx, ref inDz); // W = forward of the facing in an area; north on the map
            var cur = cursor.Position;
            var dir = new Vector3(inDx, 0f, inDz).normalized;
            if (!cursor.Space.Trace(cur, cur + dir * step, slide, out var traced)) return false;
            cursor.Position = traced;
            return true;
        }
    }
}
