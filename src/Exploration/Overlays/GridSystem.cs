using System.Collections.Generic;
using System.Text;
using Kingmaker.Controllers; // FogOfWarController
using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The tiled-space lens: describes the cell the cursor is in — direction + distance from the player to
    /// the cell centre, walkable / edge / slope, fog, and any visible things standing on it. Owns the cell
    /// size (which <see cref="TileStep"/> reads). Produces a single Tile-context announcement (the whole
    /// line) for parity; later it can split into individually-toggleable fragments. The elevation change
    /// ("up 5 ft") is computed against the last cell it described, so a step reports the slope and a
    /// re-announce of the same cell doesn't.
    /// </summary>
    internal sealed class GridSystem : OverlaySystem
    {
        public override string Name => "Grid";

        private const float TileFeet = 5f;                       // one tabletop square
        public float CellSize => TileFeet * Geo.MetresPerFoot;   // world metres per tile
        private float Half => CellSize * 0.5f;
        private const float LevelGap = 3f;                       // |Δy| treated as a different level

        private float? _lastHeight; // surface height at the last described cell (for slope deltas)

        public override void OnExit(Overlay overlay) => _lastHeight = null;

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (ctx.Want != AnnouncementContext.Tile) yield break;

            var centre = ctx.Cursor;            // already snapped to a cell centre by TileStep
            float fromY = _lastHeight ?? centre.y;
            var s = NavmeshProbe.Sample(centre.x, centre.z, centre.y);

            var sb = new StringBuilder();

            // direction + distance from the player (feet)
            var bearing = Geo.Bearing(ctx.Reference, centre);
            sb.Append(bearing == "here" ? "here" : bearing + ", " + Geo.FeetStr(Geo.Distance(ctx.Reference, centre)));
            var vert = Geo.Vertical(ctx.Reference, centre);
            if (vert != null) sb.Append(", ").Append(vert);

            sb.Append("; ").Append(Terrain(s, fromY, centre));

            if (FogOfWarController.IsInFogOfWar(centre)) sb.Append("; fog of war");

            var contents = Contents(centre);
            if (contents.Count > 0) sb.Append("; ").Append(string.Join(", ", contents.ToArray()));

            sb.Append("; ").Append(Geo.Raw(centre));

            _lastHeight = centre.y;
            yield return new OverlayAnnouncement(AnnouncementContext.Tile, Message.Raw(sb.ToString()));
        }

        private string Terrain(NavmeshProbe.Surface s, float fromY, Vector3 centre)
        {
            if (!s.OnNavmesh)
            {
                // Off the walkable surface at this height: an edge (a walkable floor well below) or a wall.
                if (NavmeshProbe.FloorBelow(centre.x, centre.z, centre.y, out var below))
                {
                    float drop = centre.y - below.y;
                    if (Geo.Feet(drop) >= 2f) return "edge, drop " + Geo.FeetStr(drop);
                }
                return "not walkable";
            }

            float dy = s.Point.y - fromY;
            if (Geo.Feet(Mathf.Abs(dy)) >= 1f)
                return "walkable, " + (dy > 0f ? "up " : "down ") + Geo.FeetStr(Mathf.Abs(dy));
            return "walkable";
        }

        // Visible things whose footprint overlaps this cell and that are on roughly this level.
        private List<string> Contents(Vector3 centre)
        {
            var names = new List<string>();
            foreach (var item in WorldModel.Items)
            {
                if (!item.IsVisible) continue;
                var p = item.Position;
                if (Mathf.Abs(p.y - centre.y) > LevelGap) continue;
                if (!OverlapsTile(p, item.Footprint, centre)) continue;
                var name = string.IsNullOrEmpty(item.Name) ? "object" : item.Name;
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        // Circle (entity footprint) vs the cell's square, in XZ.
        private bool OverlapsTile(Vector3 pos, float radius, Vector3 centre)
        {
            float dx = Mathf.Max(Mathf.Abs(pos.x - centre.x) - Half, 0f);
            float dz = Mathf.Max(Mathf.Abs(pos.z - centre.z) - Half, 0f);
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
