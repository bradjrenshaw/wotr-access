using System.Collections.Generic;
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A movement cursor: ONE object per context (<see cref="Area"/>, <see cref="WorldMap"/>), owned by
    /// nobody but itself, not by an overlay (the cursor refactor, steps three and four). It holds the point
    /// (through its <see cref="CursorSpace"/>, which is all that differs between contexts), the
    /// <see cref="MovementMode"/>s that move it (one per input slot, resolved from the cursor settings for
    /// its context), the "is it moving" signal, the idle-settle logic, and the verbs (recenter, follow a
    /// level, announce). Overlays are LENSES over it: whichever overlay is engaged composes the readout
    /// (<see cref="Overlay.Compose"/>) and plays audio around the point; the cursor itself never changes
    /// when the user cycles overlays — only the lens does, and the modes re-resolve if that overlay
    /// customized its cursor settings.
    /// </summary>
    internal sealed class Cursor
    {
        /// <summary>The in-area cursor: the shared world point the scanner plants and move-to-cursor walks to.</summary>
        public static Cursor Area { get; private set; } = new Cursor(new AreaSpace(), CursorSettings.Exploration);

        /// <summary>The world-map cursor: its own point over the flat map, in miles.</summary>
        public static Cursor WorldMap { get; private set; } = new Cursor(new GlobalMapSpace(), CursorSettings.WorldMap);

        /// <summary>Module load/reload: fresh cursors (modes, settle, motion, the map's placed point).</summary>
        public static void ResetAll()
        {
            Area = new Cursor(new AreaSpace(), CursorSettings.Exploration);
            WorldMap = new Cursor(new GlobalMapSpace(), CursorSettings.WorldMap);
        }

        public CursorSpace Space { get; }
        private readonly string _settingsContext; // CursorSettings.Exploration / WorldMap / Battles
        private readonly List<MovementMode> _modes = new List<MovementMode>();

        public Cursor(CursorSpace space, string settingsContext)
        {
            Space = space;
            _settingsContext = settingsContext;
            TileCell = space.DefaultCell * space.Unit;
        }

        /// <summary>The point, in world units. Falls back to the reference when nothing's set it yet.</summary>
        public Vector3 Position
        {
            get => Space.Get();
            set => Space.Set(value);
        }

        /// <summary>The origin for relative readouts and recenter (player / acting unit; traveler).</summary>
        public Vector3 Reference => Space.Reference;

        public IReadOnlyList<MovementMode> Modes => _modes;

        /// <summary>The tile edge (world units) the cursor last stepped by — what a tile-context readout
        /// should describe. Each tiled slot has its own size; the slot that moved last sets this. Seeded
        /// from the first tiled slot when the modes resolve.</summary>
        public float TileCell { get; set; }

        /// <summary>The lens the readout goes through: the engaged overlay, or null when overlays are off.</summary>
        private static Overlay Lens => OverlayManager.ActiveOverlay;

        // ---- movement modes (resolved from the cursor settings) ----

        // Movement is driven by each slot's "mode" choice in this cursor's context of the cursor
        // settings: the engaged overlay's custom copy when it has one, else the shared defaults
        // (CursorSettings.Context). The modes are rebuilt whenever that context object changes (the
        // user cycled to an overlay with its own cursor copy, customized, reset) or a dropdown changed
        // (Invalidate, via CursorSettings.Changed) — checked each tick by reference, so it costs nothing.
        private CategorySetting _boundContext;
        private bool _dirty = true;

        /// <summary>Rebuild the modes on the next tick (a mode dropdown changed somewhere).</summary>
        public void Invalidate() => _dirty = true;

        private void EnsureModes()
        {
            var ctx = CursorSettings.Context(_settingsContext);
            if (!_dirty && ReferenceEquals(ctx, _boundContext)) return;
            _dirty = false;
            _boundContext = ctx;
            foreach (var m in _modes) m.OnExit(this);
            _modes.Clear();
            TileCell = CursorSettings.DefaultTileCell(ctx, Space);
            foreach (var slot in CursorKeys.Slots)
            {
                var slotCat = CursorSettings.Slot(ctx, slot);
                var id = CursorSettings.Mode(slotCat);
                if (id == CursorSettings.ModeContinuous) _modes.Add(new ContinuousGlide(slot, slotCat, ctx));
                else if (id == CursorSettings.ModeTiled) _modes.Add(new TileStep(slot, slotCat));
            }
            foreach (var m in _modes) m.OnEnter(this);
        }

        /// <summary>The movement mode bound to a slot, or null. (One mode per slot in practice.)</summary>
        public MovementMode ModeFor(MovementSlot slot)
        {
            foreach (var m in _modes) if (m.Slot == slot) return m;
            return null;
        }

        private MovementMode PrimaryMode => ModeFor(MovementSlot.Primary);
        private AnnouncementContext PrimaryContext => PrimaryMode?.Context ?? AnnouncementContext.Point;

        /// <summary>A slot's held movement keys as one vector — zero unless this cursor's own screen is on
        /// top (the explore.* actions are shared across the in-area, local-map and world-map screens).</summary>
        public void HeldVector(MovementSlot slot, out int dx, out int dz)
        {
            dx = 0; dz = 0;
            if (!Space.OwnsKeys) return;
            CursorKeys.HeldVectorRaw(slot, out dx, out dz);
        }

        /// <summary>Whether the player is holding this cursor's movement keys for any ACTIVE slot — "trying
        /// to move" even when blocked (against a wall). Goes through the input-action held state
        /// (CursorKeys → InputManager.Held), so it respects bindings + category liveness. Drives the
        /// WhenMoving mode alongside actual position change.</summary>
        public bool MovementKeysHeld()
        {
            foreach (var m in _modes)
            {
                HeldVector(m.Slot, out int dx, out int dz);
                if (dx != 0 || dz != 0) return true;
            }
            return false;
        }

        // ---- per frame ----

        // "Is the cursor moving (recently)?" — drives the systems' WhenMoving mode and the terrain
        // sounds. Refreshed each tick from the fresh position; holding the keys counts as moving even
        // when blocked (against a wall), a real position change covers walking while untethered.
        private readonly MotionTracker _motion = new MotionTracker();
        public bool MovingRecently => _motion.MovingRecently;

        /// <summary>One frame in this cursor's context. Movement needs the space's own gate (control
        /// in an area; no open panel on the map); sensing continues regardless. Modes tick first so the
        /// lens reads the fresh point.</summary>
        public void Tick(float dt)
        {
            EnsureModes();
            if (Space.CanMove)
            {
                foreach (var m in _modes) m.Tick(dt, this);
                if (OverlayManager.Active) TickSettle(); else ResetSettle();
            }
            else
            {
                foreach (var m in _modes) m.Idle(this);
                ResetSettle();
            }
            _motion.Update(Position, dt, MovementKeysHeld());
        }

        /// <summary>Out of this cursor's context: no movement, no motion, no pending settle.</summary>
        public void Idle()
        {
            foreach (var m in _modes) m.Idle(this);
            _motion.Reset();
            ResetSettle();
        }

        // ---- verbs ----

        /// <summary>Back to the reference (a stepping mode snaps to its cell), then describe it.</summary>
        public void Recenter()
        {
            EnsureModes();
            var m = PrimaryMode;
            if (m != null) m.Recenter(this); else Position = Reference;
            Announce(PrimaryContext, ReadoutTrigger.Demand);
        }

        /// <summary>Plant the point somewhere (a jump to the review target), then describe it.</summary>
        public void JumpTo(Vector3 p)
        {
            EnsureModes();
            Position = p;
            Announce(PrimaryContext, ReadoutTrigger.Demand);
        }

        public void VerticalFollow(int dir)
        {
            EnsureModes();
            var m = PrimaryMode;
            var r = m != null ? m.VerticalFollow(dir, this) : VerticalResult.Unsupported;
            if (r == VerticalResult.Moved) Announce(PrimaryContext, ReadoutTrigger.Demand);
            else if (r == VerticalResult.NoSurface) Tts.Speak(Loc.T(dir < 0 ? "overlay.no_surface_below" : "overlay.no_surface_above"), interrupt: true);
        }

        public void AnnounceCurrent() { EnsureModes(); Announce(PrimaryContext, ReadoutTrigger.Demand); }

        // ---- the ONE readout pipeline ----

        /// <summary>Describe the point through the engaged lens: the overlay composes its systems' parts
        /// for this context and trigger into one line (<see cref="Overlay.Compose"/>); the cursor speaks it.
        /// A Settle with nothing to say stays silent; Step and Demand interrupt. No lens → silence.</summary>
        public void Announce(AnnouncementContext want, ReadoutTrigger trigger)
        {
            var lens = Lens;
            if (lens == null) return;
            var ctx = new OverlayContext(lens, Position, Reference, want, trigger, TileCell);
            var text = lens.Compose(ctx);
            if (string.IsNullOrEmpty(text)) return;
            if (trigger == ReadoutTrigger.Step) _stepSpoken = true;
            Tts.Speak(text, interrupt: trigger != ReadoutTrigger.Settle);
        }

        // ---- settle: the continuous glide's "I stopped" readout ----

        private Vector3 _settleLast;
        private bool _settleHas, _settleArmed, _stepSpoken, _settleRequested;

        /// <summary>A system asks for the next idle readout even without movement (an ability began
        /// aiming with the cursor already on its target).</summary>
        public void RequestSettle() => _settleRequested = true;

        // Arm on any cursor movement; fire once when the keys are released and the position has come to
        // rest — unless a discrete mode already announced its landing during this motion (a tile step
        // reads itself). Frozen while the HUD owns the arrows (a held arrow there is UI nav).
        private void TickSettle()
        {
            var p = Position;
            if (!_settleHas) { _settleHas = true; _settleLast = p; return; }
            if ((p - _settleLast).sqrMagnitude > 1e-4f) { _settleLast = p; _settleArmed = true; return; }
            if (_settleRequested) { _settleRequested = false; _settleArmed = true; _stepSpoken = false; }
            if (!_settleArmed || MovementKeysHeld() || WrathAccess.UI.Navigation.HasFocus) return;
            _settleArmed = false;
            bool stepped = _stepSpoken; _stepSpoken = false;
            if (!stepped) Announce(AnnouncementContext.Point, ReadoutTrigger.Settle);
        }

        private void ResetSettle() { _settleHas = false; _settleArmed = false; _stepSpoken = false; _settleRequested = false; }

        /// <summary>The in-area reference unit's live position — the origin for relative readouts and
        /// recenter. In turn-based that's the acting unit (so "c" lands on whoever's turn it is); otherwise
        /// the main character.</summary>
        public static Vector3 PlayerPosition
        {
            get
            {
                var p = Kingmaker.Game.Instance?.Player;
                var u = WrathAccess.Exploration.CombatMode.ReferenceUnit
                    ?? (p != null ? p.MainCharacter.Value : null);
                return WrathAccess.Exploration.Geo.Live(u);
            }
        }
    }
}
