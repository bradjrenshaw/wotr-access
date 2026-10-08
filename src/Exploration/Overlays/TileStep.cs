using UnityEngine;
using WrathAccess.Input; // OsKeyboard (typematic cadence)

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Walks the cursor across an imaginary grid with the arrow keys (one tile = a tabletop square in an
    /// area; a tile of so many miles on the world map). The cursor is a "standing position": where the
    /// space has a surface its height follows it, and at a level boundary it does NOT fall — it keeps its
    /// height so the player can feel the edge. Stacked levels are reached by a connected ramp or an
    /// explicit follow-down/up (<see cref="VerticalFollow"/>). The cell size is the SLOT's (its tiled
    /// subtree in the cursor settings), so a coarse and a fine slot can coexist; each action records it on
    /// the cursor for the readout. It re-snaps from the cursor's point each action, so a jump made
    /// elsewhere (the scanner's Home) is honoured. All geometry goes through the cursor's
    /// <see cref="CursorSpace"/>; the landing readout is the engaged overlay's job.
    /// </summary>
    internal sealed class TileStep : MovementMode
    {
        private readonly MovementSlot _slot;
        private readonly WrathAccess.Settings.CategorySetting _slotCat;
        public TileStep(MovementSlot slot, WrathAccess.Settings.CategorySetting slotCat) { _slot = slot; _slotCat = slotCat; }

        public override string Name => "Tile stepping";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Tile;

        // Read live each action (the setting may change mid-session); the readout sees the same value.
        private float Cell(Cursor cursor)
        {
            float cell = CursorSettings.TiledCellWorld(_slotCat, cursor.Space);
            cursor.TileCell = cell;
            return cell;
        }

        private static float Snap(float v, float cell) => (Mathf.Floor(v / cell) + 0.5f) * cell;

        public override void OnEnter(Cursor cursor) => Resync(cursor); // snap to the nearest cell centre

        // Stepping POLLS the slot's held arrows as one vector (so Up+Right = a single diagonal step),
        // with its own typematic cadence (the user's OS delay/rate): one step on press, a pause, then
        // repeats while held. Per-action auto-repeat can't do this — two held keys would repeat
        // independently and zigzag at double speed.
        private bool _holding;
        private float _nextStep;

        public override void Idle(Cursor cursor) => _holding = false; // the next press re-arms the first step

        public override void Tick(float dt, Cursor cursor)
        {
            // No HUD-focus gate needed: with the HUD focused the primary arrows are SHADOWED by the UI
            // category (InputManager.Held reads live bindings only), while the secondary slot keeps moving.
            if (!OverlayManager.Active) { _holding = false; return; }
            cursor.HeldVector(_slot, out int dx, out int dz);
            if (dx == 0 && dz == 0) { _holding = false; return; }
            // W steps toward the space's "up" (the facing in an area, north on the map); the GRID stays
            // world-aligned (a 45° facing walks diagonals).
            cursor.Space.StepToWorld(ref dx, ref dz);

            // A diagonal tile is sqrt(2) longer than a cardinal one; stretch the repeat interval to
            // match so held-diagonal GROUND speed equals cardinal (the step itself stays on-grid).
            float stretch = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
            float now = Time.unscaledTime;
            if (!_holding)
            {
                _holding = true;
                _nextStep = now + OsKeyboard.InitialDelay;
                Step(dx, dz, cursor);
            }
            else if (now >= _nextStep)
            {
                _nextStep = now + OsKeyboard.RepeatInterval * stretch;
                Step(dx, dz, cursor);
            }
        }

        private void Step(int dx, int dz, Cursor cursor)
        {
            float cell = Cell(cursor);
            var p = cursor.Position;
            cursor.Position = cursor.Space.Land(Snap(p.x, cell) + dx * cell, Snap(p.z, cell) + dz * cell, p.y);
            cursor.Announce(Context, ReadoutTrigger.Step); // the landing readout (the lens composes it)
        }

        public override void Recenter(Cursor cursor)
        {
            float cell = Cell(cursor);
            var p = cursor.Reference;
            cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }

        public override VerticalResult VerticalFollow(int dir, Cursor cursor)
        {
            float cell = Cell(cursor);
            var p = cursor.Position;
            var r = cursor.Space.FollowLevel(Snap(p.x, cell), Snap(p.z, cell), p.y, dir, out var at);
            if (r == VerticalResult.Moved) cursor.Position = at;
            return r;
        }

        // Snap the cursor onto a cell centre without moving it (keeps grid + shared point aligned).
        private void Resync(Cursor cursor)
        {
            float cell = Cell(cursor);
            var p = cursor.Position;
            cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }
    }
}
