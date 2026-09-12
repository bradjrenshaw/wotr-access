using Kingmaker;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The MAP frame: north as the GAME defines it — the direction that is SCREEN-UP at the default
    /// camera, up on the local map, and N on the compass. Every area carries a <c>LocalMapRotation</c>;
    /// the reset-camera key turns the camera RIG to that yaw, but the rig is mounted looking BACK
    /// (measured live: rig yaw 270 → screen-up = world yaw 90), and the local map renders at
    /// LocalMapRotation − 180 too. So map north = world yaw <c>LocalMapRotation − 180</c> — the frame
    /// the game's dialogue, journal and every walkthrough use (the Market Square is 270: its north is
    /// world east; a rotation-0 area's north is world SOUTH). Spoken bearings, the listener's default
    /// facing and the map-screen cursor keys all live in this frame (user decision 2026-09-11: "our
    /// north should match the game's north"; the 180° correction 2026-09-12 after guides read flipped),
    /// so "the door to the north" agrees with the game's own text and with the key that walks there.
    /// Internally positions stay world XZ; conversion happens at the edges — <see cref="Directions"/>
    /// for words, <see cref="ListenerFrame"/> for movement.
    /// </summary>
    internal static class MapFrame
    {
        private static float _offset;
        private static int _frame = -1;

        /// <summary>World yaw of map north (degrees; the area's LocalMapRotation − 180 — see the class
        /// note), 180 when no area. Cached per frame — read from the game each new frame.</summary>
        public static float Offset
        {
            get
            {
                if (_frame != Time.frameCount)
                {
                    _frame = Time.frameCount;
                    try { _offset = Normalize((Game.Instance?.CurrentlyLoadedArea?.LocalMapRotation ?? 0f) - 180f); }
                    catch { _offset = 180f; }
                }
                return _offset;
            }
        }

        /// <summary>A world yaw/bearing (0 = +Z) as a map bearing (0 = the game's north).</summary>
        public static float ToMap(float worldDeg) => Normalize(worldDeg - Offset);

        /// <summary>A map bearing back to world yaw.</summary>
        public static float ToWorld(float mapDeg) => Normalize(mapDeg + Offset);

        public static float Normalize(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        /// <summary>Rotate a (right, forward) input vector into world XZ so that "forward" points at
        /// world yaw <paramref name="yawDeg"/>.</summary>
        public static void RotateInput(float yawDeg, ref float dx, ref float dz)
        {
            if (yawDeg == 0f) return;
            float r = yawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            float wx = dx * c + dz * s;
            float wz = -dx * s + dz * c;
            dx = wx; dz = wz;
        }

        /// <summary>Tiled variant of <see cref="RotateInput"/>: rotate a held step vector and snap it
        /// back onto the 8 world grid directions (a 45° frame turns cardinals into exact diagonals).</summary>
        public static void RotateStep(float yawDeg, ref int dx, ref int dz)
        {
            if (yawDeg == 0f || (dx == 0 && dz == 0)) return;
            float fx = dx, fz = dz;
            RotateInput(yawDeg, ref fx, ref fz);
            dx = Mathf.RoundToInt(Mathf.Clamp(fx, -1f, 1f));
            dz = Mathf.RoundToInt(Mathf.Clamp(fz, -1f, 1f));
        }

        /// <summary>Map-frame cursor keys (the map screens: north-up in the GAME's north) → world.</summary>
        public static void InputToWorld(ref float dx, ref float dz) => RotateInput(Offset, ref dx, ref dz);
        public static void StepToWorld(ref int dx, ref int dz) => RotateStep(Offset, ref dx, ref dz);
    }
}
