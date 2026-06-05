using System;
using System.Collections.Generic;
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

        public string Name { get; }
        public Cursor Cursor { get; } = new Cursor();

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

        // ---- lifecycle ----

        public void OnEnter() { Cursor.OnEnter(this); foreach (var s in _systems) s.OnEnter(this); }
        public void OnExit() { foreach (var s in _systems) s.OnExit(this); Cursor.OnExit(this); }

        // Movement modes tick first (they update the cursor) so systems read the fresh position.
        public void Tick(float dt)
        {
            Cursor.Tick(dt, this);
            foreach (var s in _systems) s.Tick(dt, this);
        }

        // ---- input ----

        private MovementMode PrimaryMode => Cursor.ModeFor(MovementSlot.Primary);
        private AnnouncementContext PrimaryContext => PrimaryMode?.Context ?? AnnouncementContext.Point;

        /// <summary>A directional key on the given slot — routed to that slot's movement mode. A discrete
        /// stepper announces the new spot in its context; a continuous glider stays silent (moves in Tick).</summary>
        public void Move(MovementSlot slot, NavDirection dir)
        {
            var mode = Cursor.ModeFor(slot);
            if (mode == null) return;
            mode.OnDirection(dir, this);
            if (mode.AnnouncesOnMove) Announce(mode.Context);
        }

        public void Recenter()
        {
            var m = PrimaryMode;
            if (m != null) m.Recenter(this); else Cursor.Recenter();
            Announce(PrimaryContext);
        }

        public void VerticalFollow(int dir)
        {
            var m = PrimaryMode;
            var r = m != null ? m.VerticalFollow(dir, this) : VerticalResult.Unsupported;
            if (r == VerticalResult.Moved) Announce(PrimaryContext);
            else if (r == VerticalResult.NoSurface) Tts.Speak("No surface " + (dir < 0 ? "below" : "above"), interrupt: true);
        }

        public void AnnounceCurrent() => Announce(PrimaryContext);

        // ---- announce pipeline ----

        /// <summary>Gather every system's announcements, keep those describing the requested context, and
        /// speak the composed line.</summary>
        public void Announce(AnnouncementContext want)
        {
            var ctx = new OverlayContext(this, Cursor.Position, Cursor.PlayerPosition, want);
            var spoken = new List<Message>();
            foreach (var s in _systems)
                foreach (var a in s.Announce(ctx))
                    if (a != null && a.Context == want && a.Text != null) spoken.Add(a.Text);
            if (spoken.Count > 0)
            {
                var line = Message.Join("; ", spoken.ToArray()).Resolve();
                if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: true);
            }
        }
    }
}
