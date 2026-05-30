using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Controllers; // FogOfWarController
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using UnityEngine;
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Lays an imaginary grid over the continuous world and walks a cursor across it with the arrow keys
    /// (one tile = 5 ft, a tabletop square, so counting tiles maps onto movement/reach/ranges). Each tile
    /// announces its direction + distance from the player, whether it's walkable, slope/edge info, and any
    /// visible thing standing on it.
    ///
    /// The cursor is a "standing position", not a free 3D point: its height follows the walkable surface
    /// (announcing the rise/fall as you cross slopes and stairs), and at a level boundary it does NOT fall
    /// — the edge is announced and the cursor keeps its height. Stacked levels are reached either by
    /// walking a connected ramp (height follows naturally) or with an explicit follow-down/up
    /// (<see cref="VerticalFollow"/>). This mirrors the game, where movement never leaves the navmesh.
    /// The cursor may sit on non-walkable tiles so the player can feel where walls and edges are.
    /// Raw position is appended as a testing aid for now.
    /// </summary>
    internal sealed class VirtualTileView : Overlay
    {
        public override string Name => "Tile view";

        private const float TileFeet = 5f;                          // one tabletop square
        private static float TileSize => TileFeet * Geo.MetresPerFoot; // world metres per tile
        private static float Half => TileSize * 0.5f;
        private const float LevelGap = 3f;       // |Δy| (metres) treated as a different level (game's own)

        private float _x, _z;  // tile-centre world XZ (re-synced from the shared cursor each action)
        private float _y;      // tracked surface height — follows slopes, kept across edges/voids

        private static UnitEntityData Leader
        {
            get { var p = Game.Instance?.Player; return p != null ? p.MainCharacter.Value : null; }
        }
        private static Vector3 Reference => Geo.Live(Leader);

        // On entering, resume from the shared cursor if one's set (so the scanner's Home target and the
        // tile grid are the same point), snapping it to the nearest tile centre; otherwise start on the
        // player. Recenter always jumps back to the player.
        public override void OnEnter()
        {
            if (Cursor.Has) Adopt(Cursor.Position.Value);
            else CenterOnPlayer(announce: true);
        }

        public override void Recenter() => CenterOnPlayer(announce: true);

        private void CenterOnPlayer(bool announce)
        {
            var p = Reference;
            _x = SnapCentre(p.x); _z = SnapCentre(p.z); _y = p.y;
            Commit();
            if (announce) Speak("Cursor on you. " + DescribeCurrent());
        }

        private void Adopt(Vector3 c)
        {
            _x = SnapCentre(c.x); _z = SnapCentre(c.z); _y = c.y;
            Commit();
            Speak("Tile cursor. " + DescribeCurrent());
        }

        // Publish the tile cursor as the one shared cursor, so move-to-cursor (Backspace) walks here.
        private void Commit() => Cursor.Set(new Vector3(_x, _y, _z));

        // Resume from the shared cursor before each action, so a jump made elsewhere (the scanner's Home)
        // is honoured rather than clobbered by our stale cached tile. Idempotent after our own moves —
        // we commit tile centres, which re-snap to themselves — and falls back to the player on a cold
        // start (no cursor yet).
        private void EnsureSynced()
        {
            if (Cursor.Has)
            {
                var c = Cursor.Position.Value;
                _x = SnapCentre(c.x); _z = SnapCentre(c.z); _y = c.y;
            }
            else CenterOnPlayer(announce: false);
        }

        public override void Move(NavDirection dir)
        {
            EnsureSynced();
            float fromY = _y;
            switch (dir)
            {
                case NavDirection.Up: _z += TileSize; break;    // +Z = north
                case NavDirection.Down: _z -= TileSize; break;
                case NavDirection.Right: _x += TileSize; break; // +X = east
                case NavDirection.Left: _x -= TileSize; break;
            }
            var s = NavmeshProbe.Sample(_x, _z, _y);
            if (s.OnNavmesh) _y = s.Point.y; // follow the surface; otherwise keep height (never fall)
            Commit();
            Speak(Describe(s, fromY));
        }

        public override void AnnounceCurrent()
        {
            EnsureSynced();
            Speak(DescribeCurrent());
        }

        // Follow a surface to the level below (-1) or above (+1) at the cursor's column — the explicit way
        // to change levels where they stack. Never automatic.
        public override void VerticalFollow(int dir)
        {
            EnsureSynced();
            Vector3 floor;
            bool found = dir < 0
                ? NavmeshProbe.FloorBelow(_x, _z, _y, out floor)
                : NavmeshProbe.FloorAbove(_x, _z, _y, out floor);
            if (!found) { Speak("No surface " + (dir < 0 ? "below" : "above")); return; }
            float fromY = _y; _y = floor.y;
            Commit();
            Speak((dir < 0 ? "Down " : "Up ") + Geo.FeetStr(Mathf.Abs(fromY - _y)) + ". " + DescribeCurrent());
        }

        private string DescribeCurrent()
        {
            var s = NavmeshProbe.Sample(_x, _z, _y);
            float fromY = _y;
            if (s.OnNavmesh) { _y = s.Point.y; Commit(); }
            return Describe(s, fromY);
        }

        // ---- readout ----

        private string Describe(NavmeshProbe.Surface s, float fromY)
        {
            var centre = new Vector3(_x, _y, _z);
            var sb = new StringBuilder();

            // direction + distance from the player (in feet)
            var bearing = Geo.Bearing(Reference, centre);
            sb.Append(bearing == "here" ? "here" : bearing + ", " + Geo.FeetStr(Geo.Distance(Reference, centre)));
            var vert = Geo.Vertical(Reference, centre);
            if (vert != null) sb.Append(", ").Append(vert);

            sb.Append("; ").Append(Terrain(s, fromY));

            // Currently out of the party's sight (vision radius + LoS). Informational only — you can still
            // move into fog; it just means we can't see what's there right now (entities are hidden too).
            if (FogOfWarController.IsInFogOfWar(centre)) sb.Append("; fog of war");

            var contents = Contents();
            if (contents.Count > 0) sb.Append("; ").Append(string.Join(", ", contents.ToArray()));

            sb.Append("; ").Append(Geo.Raw(centre));
            return sb.ToString();
        }

        private string Terrain(NavmeshProbe.Surface s, float fromY)
        {
            if (!s.OnNavmesh)
            {
                // Off the walkable surface at this height. If a walkable floor sits well below, it's an
                // edge/drop; otherwise just a wall / nothing to stand on.
                if (NavmeshProbe.FloorBelow(_x, _z, _y, out var below))
                {
                    float drop = _y - below.y;
                    if (Geo.Feet(drop) >= 2f) return "edge, drop " + Geo.FeetStr(drop);
                }
                return "not walkable";
            }

            float dy = s.Point.y - fromY;
            if (Geo.Feet(Mathf.Abs(dy)) >= 1f)
                return "walkable, " + (dy > 0f ? "up " : "down ") + Geo.FeetStr(Mathf.Abs(dy));
            return "walkable";
        }

        // Visible things whose footprint overlaps this tile and that are on roughly this level (so we don't
        // report an upper-floor creature while standing below it). Large entities span several tiles.
        private List<string> Contents()
        {
            var names = new List<string>();
            foreach (var item in WorldModel.Items)
            {
                if (!item.IsVisible) continue; // the model holds all in-area items; we surface only visible
                var p = item.Position;
                if (Mathf.Abs(p.y - _y) > LevelGap) continue;
                if (!OverlapsTile(p, item.Footprint)) continue;
                var name = string.IsNullOrEmpty(item.Name) ? "object" : item.Name;
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        // Circle (entity footprint) vs the tile's square, in XZ.
        private bool OverlapsTile(Vector3 pos, float radius)
        {
            float dx = Mathf.Max(Mathf.Abs(pos.x - _x) - Half, 0f);
            float dz = Mathf.Max(Mathf.Abs(pos.z - _z) - Half, 0f);
            return dx * dx + dz * dz <= radius * radius;
        }

        private static float SnapCentre(float v) => (Mathf.Floor(v / TileSize) + 0.5f) * TileSize;

        private static void Speak(string msg) { if (!string.IsNullOrEmpty(msg)) Tts.Speak(msg, interrupt: true); }
    }
}
