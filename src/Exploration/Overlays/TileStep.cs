using UnityEngine;
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Walks the cursor across an imaginary 5-ft grid with the arrow keys (one tile = a tabletop square).
    /// The cursor is a "standing position": its height follows the walkable surface, and at a level
    /// boundary it does NOT fall — it keeps its height so the player can feel the edge. Stacked levels are
    /// reached by a connected ramp or an explicit follow-down/up (<see cref="VerticalFollow"/>). The cell
    /// size comes from the sibling <see cref="GridSystem"/> (one per overlay). It re-snaps from the shared
    /// cursor each action, so a jump made elsewhere (the scanner's Home) is honoured. Tile context — the
    /// readout itself is <see cref="GridSystem"/>'s job.
    /// </summary>
    internal sealed class TileStep : MovementMode
    {
        public override string Name => "Tile stepping";
        public override MovementSlot Slot => MovementSlot.Primary;
        public override AnnouncementContext Context => AnnouncementContext.Tile;

        private static float Cell(Overlay overlay)
            => overlay.Get<GridSystem>()?.CellSize ?? (5f * Geo.MetresPerFoot);

        private static float Snap(float v, float cell) => (Mathf.Floor(v / cell) + 0.5f) * cell;

        public override void OnEnter(Overlay overlay) => Resync(overlay); // snap to the nearest cell centre

        public override void OnDirection(NavDirection dir, Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            float x = Snap(p.x, cell), z = Snap(p.z, cell), y = p.y;
            switch (dir)
            {
                case NavDirection.Up: z += cell; break;    // +Z = north
                case NavDirection.Down: z -= cell; break;
                case NavDirection.Right: x += cell; break; // +X = east
                case NavDirection.Left: x -= cell; break;
            }
            var s = NavmeshProbe.Sample(x, z, y);
            if (s.OnNavmesh) y = s.Point.y; // follow the surface; otherwise keep height (never fall)
            overlay.Cursor.Position = new Vector3(x, y, z);
        }

        public override void Recenter(Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = Cursor.PlayerPosition;
            overlay.Cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }

        public override VerticalResult VerticalFollow(int dir, Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            float x = Snap(p.x, cell), z = Snap(p.z, cell), y = p.y;
            bool found = dir < 0
                ? NavmeshProbe.FloorBelow(x, z, y, out var floor)
                : NavmeshProbe.FloorAbove(x, z, y, out floor);
            if (!found) return VerticalResult.NoSurface;
            overlay.Cursor.Position = new Vector3(x, floor.y, z);
            return VerticalResult.Moved;
        }

        // Snap the shared cursor onto a cell centre without moving it (keeps grid + shared cursor aligned).
        private static void Resync(Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            overlay.Cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }
    }
}
