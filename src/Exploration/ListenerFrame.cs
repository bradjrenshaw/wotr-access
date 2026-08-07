using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The listener's FACING — the rotatable orientation of the virtual head (user-designed; default
    /// north, the historical fixed frame most players will keep). Q/E turn it in 45° PERSON-frame
    /// steps ("turn right" = facing yaw increases: north → east — deliberately NOT the camera-key
    /// convention, which is scene-labeled and reads inverted by ear). Everything cursor-relative
    /// rotates with it: WASD movement (W = forward of facing), the spatial pans (<see cref="ToEar"/>),
    /// the wall-tone trace directions. Spoken BEARINGS stay world-aligned by standing decision
    /// ("northeast" never silently becomes "ahead-left"); tiled steps stay ON the world grid,
    /// stepping in the facing's direction; the map screens stay north-up (their cursors don't
    /// consult the facing).
    /// </summary>
    internal static class ListenerFrame
    {
        /// <summary>Degrees, 0 = north (+Z), 90 = east — same convention as every spoken bearing.</summary>
        public static float Facing { get; private set; }

        private const float TurnSpeed = 90f; // continuous turn, degrees/second (tuned by ear)

        /// <summary>Continuous turning: Q/E poll as held (the movement-key pattern — the claim chain
        /// gates them), rotating smoothly; crossing into a new 8-point sector announces it, so turning
        /// narrates itself without spamming. Ticked from the frame loop.</summary>
        public static void Tick(float dt)
        {
            int dir = 0;
            if (WrathAccess.Input.InputManager.Held("explore.turnLeft")) dir -= 1;
            if (WrathAccess.Input.InputManager.Held("explore.turnRight")) dir += 1;
            if (dir == 0) return;

            int before = Sector();
            SetFacing(Facing + dir * TurnSpeed * dt);
            if (Sector() != before)
                Tts.Speak(Loc.T("facing.now", new { dir = Geo.DirectionWord(Facing) }), interrupt: true);
        }

        /// <summary>Shift+Q/E: snap to the NEXT 45° multiple in that direction and announce it.</summary>
        public static void StepLeft() => Step(-1);
        public static void StepRight() => Step(1);

        private static void Step(int dir)
        {
            float cur = Facing / 45f;
            float next = dir > 0 ? Mathf.Floor(cur + 1f) : Mathf.Ceil(cur - 1f);
            SetFacing(next * 45f);
            Tts.Speak(Loc.T("facing.now", new { dir = Geo.DirectionWord(Facing) }), interrupt: true);
        }

        private static void SetFacing(float f)
        {
            f %= 360f;
            if (f < 0f) f += 360f;
            Facing = f;
        }

        private static int Sector() => Mathf.RoundToInt(Facing / 45f) % 8;

        /// <summary>A WORLD ear delta (east, north) rotated into the LISTENER frame (right, ahead) —
        /// apply before any spatial pan so sounds sit around the head, not around the map.</summary>
        public static void ToEar(ref float dxEast, ref float dzNorth)
        {
            if (Facing == 0f) return;
            float r = Facing * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            float ex = dxEast * c - dzNorth * s;
            float ez = dxEast * s + dzNorth * c;
            dxEast = ex; dzNorth = ez;
        }

        /// <summary>A movement INPUT vector (right, forward) rotated into the world — W walks toward
        /// the facing. For the continuous glide (floats).</summary>
        public static void InputToWorld(ref float dx, ref float dz)
        {
            if (Facing == 0f) return;
            float r = Facing * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            float wx = dx * c + dz * s;
            float wz = -dx * s + dz * c;
            dx = wx; dz = wz;
        }

        /// <summary>Tiled variant: rotate a held step vector by the facing and snap back onto the
        /// 8 grid directions (45° facings turn cardinals into exact diagonals).</summary>
        public static void StepToWorld(ref int dx, ref int dz)
        {
            if (Facing == 0f || (dx == 0 && dz == 0)) return;
            float fx = dx, fz = dz;
            InputToWorld(ref fx, ref fz);
            dx = Mathf.RoundToInt(Mathf.Clamp(fx, -1f, 1f));
            dz = Mathf.RoundToInt(Mathf.Clamp(fz, -1f, 1f));
        }
    }
}
