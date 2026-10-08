using System.Linq;
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using UnityEngine;
using WrathAccess.Exploration.Overlays;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map MOVEMENT cursor's map-specific verbs. The cursor itself is <see cref="Overlays.Cursor.WorldMap"/>
    /// (the cursor refactor, step four — 2026-10-08): the same <see cref="Overlays.Cursor"/> class as in-area, on a
    /// <see cref="GlobalMapSpace"/> (its own point over the flat map, miles, map-north input), moved by the
    /// shared glide / tile-step modes from the cursor settings' WORLD MAP context, read out through the
    /// engaged overlay (<see cref="GlobalMapPointSystem"/>). What stays here is what only the map has:
    /// <b>Enter</b> acts on the point under the cursor, <b>/</b> jumps to the review cursor, and the
    /// point-footprint hit test the readout and the actions share. <b>C</b> recenters and <b>K</b> reads
    /// through the cursor's own verbs.
    /// </summary>
    internal static class GlobalMapCursor
    {
        private const float Padding = 0.4f; // a little extra reach past each point's footprint so it's easy to land on

        /// <summary>The cursor's point — its placed position, else the traveler's.</summary>
        public static Vector3 Position => Overlays.Cursor.WorldMap.Position;

        /// <summary>Entering the map: forget the placed point so the cursor starts on the traveler.</summary>
        public static void Reset()
        {
            (Overlays.Cursor.WorldMap.Space as GlobalMapSpace)?.Reset();
            Overlays.Cursor.WorldMap.Idle();
        }

        // ---- on-demand keys ----
        public static void Recenter() => Overlays.Cursor.WorldMap.Recenter();

        public static void JumpToReview()
        {
            var p = GlobalMapScanner.SelectedPosition;
            if (!p.HasValue) { Tts.Speak(Loc.T("worldmap.scan_none")); return; }
            Overlays.Cursor.WorldMap.JumpTo(p.Value);
        }

        // K: read what the cursor is on (manual readout).
        public static void Announce() => Overlays.Cursor.WorldMap.AnnounceCurrent();

        public static void Interact()
        {
            // Only act in the pure global-map mode. Under a rest / dialog / book-event / battle overlay the
            // overlay owns input — acting here (Go → HandleClick) restarts the journey that re-fires the
            // event, the infinite loop. (Travel "pauses" under those overlays too, so we can't rely on
            // TravelPaused alone.)
            if (!GlobalMapModel.Interactive) return;
            // Mid-journey pause (the game's move-helper Continue): Enter resumes travel, like the game's own
            // primary travel input. Otherwise act on the point under the cursor.
            if (GlobalMapModel.TravelPaused) { GlobalMapActions.ResumeTravel(); return; }
            // An army pawn sits on top of its point (the game's click lands on the pawn first).
            var a = ArmyWithin();
            if (a != null) { GlobalMapActions.ArmyInteract(a); return; }
            var p = NearestWithin();
            if (p != null) GlobalMapActions.Go(p);
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        /// <summary>The nearest point whose OWN footprint contains the cursor, or null. Each point uses its
        /// real clickable radius (below) rather than one fixed circle — a fixed 8-unit radius was far larger
        /// than a location's icon, so the cursor read "on" many overlapping points and exact selection was
        /// hard.</summary>
        public static GlobalMapPointView NearestWithin()
        {
            GlobalMapPointView best = null;
            float bd = float.MaxValue;
            var c = Position;
            foreach (var pt in GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions))
            {
                if (pt == null) continue;
                float d = Geo.Distance(c, pt.transform.position);
                if (d <= Radius(pt) && d < bd) { bd = d; best = pt; }
            }
            return best;
        }

        /// <summary>The nearest revealed army whose pawn footprint contains the cursor, or null — the same
        /// hit test as the points, on the pawn's collider (armies share the map with locations: one may be
        /// camped on a point, in which case both are "under" the cursor).</summary>
        public static GlobalMapArmyState ArmyWithin()
        {
            GlobalMapArmyState best = null;
            float bd = float.MaxValue;
            var c = Position;
            foreach (var army in GlobalMapModel.Armies)
            {
                var pos = GlobalMapActions.ArmyPosition(army);
                if (!pos.HasValue) continue;
                float d = Geo.Distance(c, pos.Value);
                if (d <= Radius(army.View) && d < bd) { bd = d; best = army; }
            }
            return best;
        }

        // A point's real clickable radius: the SphereCollider the game gives it (GlobalMapPointView.OnEnable —
        // a LOCATION's is its icon's half-width `renderer.bounds.extents.x`; a WAYPOINT's is 0.5), read live
        // from the collider's world bounds (scale-adjusted) so it matches the game's actual click target,
        // plus a small <see cref="Padding"/> so it's comfortable to land on. A modest fallback covers the rare
        // frame before the collider is built.
        private static float Radius(Component pt)
        {
            var col = pt != null ? pt.GetComponent<Collider>() : null;
            if (col != null)
            {
                var e = col.bounds.extents;
                float r = Mathf.Max(e.x, e.z);
                if (r > 0.01f) return r + Padding;
            }
            return 1.5f;
        }
    }
}
