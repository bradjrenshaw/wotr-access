using System.IO;
using System.Linq;
using Kingmaker.Globalmap.View;
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays; // OverlayAudio
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map MOVEMENT cursor — free-roam over the XZ plane with WASD/arrows (the analogue of the
    /// in-area cursor; isolated, no navmesh). A SEPARATE point from the in-area <see cref="Cursor"/> so it
    /// never pollutes it. Gliding plays the same object enter/leave cue as in-area when it crosses onto/off
    /// a point (respecting the shared object-cue volume), so the feel matches. <b>Enter</b> acts on the
    /// point under it; <b>C</b> recenters on the party; <b>K</b> reads it; <b>/</b> jumps to the review
    /// cursor. Sonar, the secondary (Shift) cursor, and a configurable miles speed come later.
    /// </summary>
    internal static class GlobalMapCursor
    {
        private const float SpeedUnitsPerSec = 18f; // tunable by feel; a settings entry comes later
        private const float SnapRadius = 8f;        // "on" / interact / cue radius around a point (world units)

        private static Vector3? _pos;
        private static GlobalMapPointView _inside; // the point the cursor is on (for the enter/leave cue)
        private static GlobalMapPointView _spoken; // last point the idle-settle readout spoke (null = armed)
        private static bool _baselined;            // don't fire the cue on the first tick / on entering the map

        public static void Reset() { _pos = null; _inside = null; _spoken = null; _baselined = false; }

        /// <summary>The cursor's point — its placed position, else the party's.</summary>
        public static Vector3 Position => _pos ?? GlobalMapModel.TravelerPos;

        // Per-frame glide + the object enter/leave cue. InputManager.Held respects the claim chain, so the
        // worldmap.cursor* keys read as held only while the cursor (not the focused list) owns the arrows.
        public static void Tick(float dt)
        {
            if (!GlobalMapModel.Active) { _inside = null; _spoken = null; _baselined = false; return; }

            HeldVector(out int ix, out int iz);
            bool moving = ix != 0 || iz != 0;
            if (moving)
            {
                if (!_pos.HasValue) _pos = GlobalMapModel.TravelerPos; // plant at the party on first move
                var dir = new Vector3(ix, 0f, iz).normalized;
                _pos = _pos.Value + dir * (SpeedUnitsPerSec * dt);
            }

            var inside = NearestWithin(SnapRadius);

            // Object cue: same wavs + shared volume as the in-area ObjectCueSystem. Fires on a change of the
            // point we're inside — enter when arriving on one (incl. straight from another), leave to none.
            if (!_baselined) { _inside = inside; _spoken = inside; _baselined = true; return; }
            if (inside != _inside) { PlayCue(inside != null); _inside = inside; }

            // Idle settle readout: gliding is too fast to narrate, but once the keys are released, speak the
            // point the cursor sits on (once), exactly like in-area's idle hover announce. Over nothing → quiet.
            if (moving || inside == null) _spoken = null;
            else if (inside != _spoken) { Tts.Speak(GlobalMapActions.InPlace(inside)); _spoken = inside; }
        }

        private static void HeldVector(out int dx, out int dz)
        {
            dx = 0; dz = 0;
            if (InputManager.Held("worldmap.cursorUp")) dz += 1;     // +Z = north
            if (InputManager.Held("worldmap.cursorDown")) dz -= 1;
            if (InputManager.Held("worldmap.cursorRight")) dx += 1;  // +X = east
            if (InputManager.Held("worldmap.cursorLeft")) dx -= 1;
        }

        private static void PlayCue(bool enter)
        {
            float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
            AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, enter ? "object_enter.wav" : "object_exit.wav"), vol);
        }

        // ---- on-demand keys ----
        public static void Recenter() { _pos = GlobalMapModel.TravelerPos; Settle(); }

        public static void JumpToReview()
        {
            var p = GlobalMapScanner.SelectedPosition;
            if (!p.HasValue) { Tts.Speak(Loc.T("worldmap.scan_none")); return; }
            _pos = p.Value;
            Settle();
        }

        // K: read what the cursor is on (manual readout).
        public static void Announce()
        {
            var p = NearestWithin(SnapRadius);
            if (p != null) Tts.Speak(GlobalMapActions.InPlace(p));
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        // Snap-and-read (recenter / jump-to-review): speak the point we land on and baseline the cue +
        // idle readout so the next Tick doesn't repeat it.
        private static void Settle()
        {
            var p = NearestWithin(SnapRadius);
            _inside = p; _spoken = p;
            if (p != null) Tts.Speak(GlobalMapActions.InPlace(p));
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        public static void Interact()
        {
            var p = NearestWithin(SnapRadius);
            if (p != null) GlobalMapActions.Go(p);
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        // The nearest point within `radius` of the cursor, or null.
        private static GlobalMapPointView NearestWithin(float radius)
        {
            GlobalMapPointView best = null;
            float bd = radius;
            var c = Position;
            foreach (var pt in GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions))
            {
                float d = Geo.Distance(c, pt.transform.position);
                if (d <= bd) { bd = d; best = pt; }
            }
            return best;
        }
    }
}
