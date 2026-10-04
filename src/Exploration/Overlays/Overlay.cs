using System;
using System.Collections.Generic;
using UnityEngine;
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// One configurable lens over the area: a <see cref="Cursor"/> (with its movement modes) plus a set of
    /// <see cref="OverlaySystem"/>s, <b>one per type</b>. The overlay owns no behavior of its own beyond
    /// fanning lifecycle/tick out, routing a movement key to the matching mode, and running the announce
    /// pipeline (gather each system's announcements for a context → speak the composed line). User-
    /// composable: an overlay is just which systems + which movement modes, with their settings.
    /// </summary>
    internal sealed class Overlay
    {
        private readonly List<OverlaySystem> _systems = new List<OverlaySystem>(); // ordered (readout order)
        private readonly Dictionary<Type, OverlaySystem> _byType = new Dictionary<Type, OverlaySystem>();

        public string Name { get; set; } // settable so a live rename updates the spoken cycle name
        public Cursor Cursor { get; } = new Cursor();

        /// <summary>This overlay's customized cursor settings (the full <see cref="CursorSettings"/>
        /// schema under overlays.&lt;id&gt;.cursor.custom), or null while it follows the shared defaults.
        /// Readers resolve through <see cref="CursorSettings.Context"/>.</summary>
        public WrathAccess.Settings.CategorySetting CursorRoot { get; set; }

        public Overlay(string name) { Name = name; }

        // ---- composition ----

        /// <summary>Add a system (one per concrete type; a duplicate replaces the prior instance).</summary>
        public Overlay With(OverlaySystem system)
        {
            if (system == null) return this;
            var t = system.GetType();
            if (_byType.TryGetValue(t, out var existing)) _systems.Remove(existing);
            _byType[t] = system;
            _systems.Add(system);
            return this;
        }

        public Overlay With(MovementMode mode) { Cursor.AddMode(mode); return this; }

        /// <summary>The single system of type T on this overlay, or null. Deterministic by one-per-type.</summary>
        public T Get<T>() where T : OverlaySystem
            => _byType.TryGetValue(typeof(T), out var s) ? (T)s : null;

        /// <summary>This overlay's system with the given settings key (e.g. "sonar"), or null — for the
        /// mode-cycle / hold hotkeys that act on a system by key on the engaged overlay.</summary>
        public OverlaySystem GetSystem(string key)
        {
            foreach (var s in _systems) if (s.Key == key) return s;
            return null;
        }

        // ---- lifecycle ----

        // Only run the systems (and the in-area cursor) that belong to the live context — so navmesh-bound
        // in-area systems stay off the world map and world-map systems stay off in areas. The in-area cursor
        // is itself in-area-only; the world map drives its own cursor (GlobalMapCursor).
        private static bool Applies(OverlayScope sys) => sys == OverlayScope.Both || sys == OverlayManager.CurrentScope;
        private static bool InAreaNow => OverlayManager.CurrentScope == OverlayScope.InArea;

        public void OnEnter()
        {
            if (InAreaNow) Cursor.OnEnter(this);
            foreach (var s in _systems) if (Applies(s.Scope)) s.OnEnter(this);
        }
        public void OnExit()
        {
            foreach (var s in _systems) if (Applies(s.Scope)) s.OnExit(this);
            TerrainSounds.Teardown();
            if (InAreaNow) Cursor.OnExit(this);
        }

        // "Is the in-area cursor moving (recently)?" — drives the systems' WhenMoving mode. Refreshed each
        // tick from the fresh cursor position; world-map systems track their own cursor instead.
        private readonly MotionTracker _cursorMotion = new MotionTracker();
        public bool CursorMovingRecently => _cursorMotion.MovingRecently;

        // Movement modes tick first (they update the cursor) so systems read the fresh position.
        public void Tick(float dt)
        {
            // Cursor MOVEMENT is the one thing gated on having control — so it can't drift during a cutscene.
            // The sensing systems keep ticking regardless (each decides what, if anything, to suppress); the
            // overlay's master gate (OverlayManager.InExploration) is context-only, not control.
            if (InAreaNow && WrathAccess.ControlState.HasControl) { Cursor.Tick(dt, this); if (OverlayManager.Active) TickSettle(); else ResetSettle(); }
            else ResetSettle();
            // Refresh the moving signal from the (now fresh) cursor before systems read ShouldPlay. Holding
            // the movement keys counts as moving even when blocked (against a wall), via the input-action
            // held state; a real position change covers walking while the cursor is untethered.
            if (InAreaNow) _cursorMotion.Update(Cursor.Position, dt, Cursor.MovementKeysHeld()); else _cursorMotion.Reset();
            foreach (var s in _systems) if (Applies(s.Scope)) s.Tick(dt, this);
            if (InAreaNow) TerrainSounds.Tick(dt, this); else TerrainSounds.Teardown();
        }

        // ---- input ----

        private MovementMode PrimaryMode => Cursor.ModeFor(MovementSlot.Primary);
        private AnnouncementContext PrimaryContext => PrimaryMode?.Context ?? AnnouncementContext.Point;

        /// <summary>A directional key on the given slot — routed to that slot's movement mode. A discrete
        /// stepper announces the new spot in its context; a continuous glider stays silent (moves in Tick).</summary>
        public void Recenter()
        {
            var m = PrimaryMode;
            if (m != null) m.Recenter(this); else Cursor.Recenter();
            Announce(PrimaryContext, ReadoutTrigger.Demand);
        }

        public void VerticalFollow(int dir)
        {
            var m = PrimaryMode;
            var r = m != null ? m.VerticalFollow(dir, this) : VerticalResult.Unsupported;
            if (r == VerticalResult.Moved) Announce(PrimaryContext, ReadoutTrigger.Demand);
            else if (r == VerticalResult.NoSurface) Tts.Speak(Loc.T(dir < 0 ? "overlay.no_surface_below" : "overlay.no_surface_above"), interrupt: true);
        }

        public void AnnounceCurrent() => Announce(PrimaryContext, ReadoutTrigger.Demand);

        // ---- the ONE readout pipeline ----

        /// <summary>Gather every system's announcements for the requested context and trigger, order
        /// them (position, effect, path, contents), drop exact repeats, and speak the composed line as
        /// one utterance. A Settle with nothing to say stays silent; Step and Demand interrupt.</summary>
        public void Announce(AnnouncementContext want, ReadoutTrigger trigger)
        {
            var ctx = new OverlayContext(this, Cursor.Position, Cursor.PlayerPosition, want, trigger, Cursor.TileCell);
            var parts = new List<OverlayAnnouncement>();
            foreach (var s in _systems)
                if (Applies(s.Scope))
                    foreach (var a in s.Announce(ctx))
                        if (a != null && a.Context == want && a.Text != null) parts.Add(a);
            if (parts.Count == 0) return;
            parts.Sort((a, b) => a.Order.CompareTo(b.Order)); // List.Sort is unstable; equal orders keep system order below
            var seen = new HashSet<string>();
            var texts = new List<string>();
            foreach (var a in parts)
            {
                var t = a.Text.Resolve();
                if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
                texts.Add(t);
            }
            if (texts.Count == 0) return;
            if (trigger == ReadoutTrigger.Step) _stepSpoken = true;
            Tts.Speak(string.Join("; ", texts.ToArray()), interrupt: trigger != ReadoutTrigger.Settle);
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
            var p = Cursor.Position;
            if (!_settleHas) { _settleHas = true; _settleLast = p; return; }
            if ((p - _settleLast).sqrMagnitude > 1e-4f) { _settleLast = p; _settleArmed = true; return; }
            if (_settleRequested) { _settleRequested = false; _settleArmed = true; _stepSpoken = false; }
            if (!_settleArmed || Cursor.MovementKeysHeld() || WrathAccess.UI.Navigation.HasFocus) return;
            _settleArmed = false;
            bool stepped = _stepSpoken; _stepSpoken = false;
            if (!stepped) Announce(AnnouncementContext.Point, ReadoutTrigger.Settle);
        }

        private void ResetSettle() { _settleHas = false; _settleArmed = false; _stepSpoken = false; _settleRequested = false; }
    }
}
