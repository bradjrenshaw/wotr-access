namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// How a <see cref="Cursor"/> moves in response to one input slot. Owned by the cursor (NOT a system,
    /// NOT an overlay), so several can drive the same cursor on different keys — e.g. continuous glide on
    /// the primary slot and tile-stepping on the tertiary. A mode's tunables come from its slot's cursor
    /// settings (speed, tile size) and its geometry from the cursor's <see cref="CursorSpace"/> (navmesh
    /// vs flat map, feet vs miles, facing vs north), so the same mode classes serve every context; the
    /// readout it triggers goes through the cursor (<see cref="Cursor.Announce"/>), which routes it to
    /// whichever overlay is engaged.
    ///
    /// All modes poll their slot's held arrows in <see cref="Tick"/> as one combined vector (see
    /// <see cref="CursorKeys"/>), so held diagonals work: <b>discrete</b> modes step on a typematic
    /// cadence and announce each landing (<see cref="AnnouncesOnMove"/> true); <b>continuous</b> modes
    /// glide per frame (false — feedback is audio, not per-frame speech).
    /// </summary>
    internal abstract class MovementMode
    {
        public abstract string Name { get; }
        public abstract MovementSlot Slot { get; }

        /// <summary>The announcement context this mode reads when it moves / is asked to describe.</summary>
        public abstract AnnouncementContext Context { get; }

        /// <summary>Whether a move triggers the cursor to speak the new position. Discrete steppers → true;
        /// continuous gliders → false (audio-driven).</summary>
        public virtual bool AnnouncesOnMove => true;

        public virtual void OnEnter(Cursor cursor) { }
        public virtual void OnExit(Cursor cursor) { }

        /// <summary>The cursor can't move right now (no control / a panel is open / another context is
        /// up): drop any held-key state so the next press starts clean.</summary>
        public virtual void Idle(Cursor cursor) { }

        /// <summary>Continuous movement: glide per frame by polling this slot's held keys.</summary>
        public virtual void Tick(float dt, Cursor cursor) { }

        /// <summary>Reset the cursor to its reference (player / traveler; modes with a grid snap to its cell).</summary>
        public virtual void Recenter(Cursor cursor) => cursor.Position = cursor.Reference;

        /// <summary>Follow a surface to the level below (-1) or above (+1). Modes without a level concept
        /// return <see cref="VerticalResult.Unsupported"/>.</summary>
        public virtual VerticalResult VerticalFollow(int dir, Cursor cursor) => VerticalResult.Unsupported;
    }
}
