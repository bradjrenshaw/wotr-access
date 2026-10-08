using System;
using System.Collections.Generic;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// One configurable LENS over the area: a set of <see cref="OverlaySystem"/>s, <b>one per type</b>, that
    /// sense and describe the surroundings of the shared in-area cursor (<see cref="Cursor.Area"/>) — or,
    /// for world-map-scoped systems, the world-map cursor. The overlay owns no cursor and no movement
    /// (the cursor refactor, step three): it fans lifecycle/tick out to its systems and composes their
    /// announcement parts into one line when the cursor asks (<see cref="Compose"/>). User-composable: an
    /// overlay is just which systems, with their settings — plus, optionally, its own copy of the cursor
    /// settings (<see cref="CursorRoot"/>), which the cursor picks up while this overlay is engaged.
    /// </summary>
    internal sealed class Overlay
    {
        private readonly List<OverlaySystem> _systems = new List<OverlaySystem>(); // ordered (readout order)
        private readonly Dictionary<Type, OverlaySystem> _byType = new Dictionary<Type, OverlaySystem>();

        public string Name { get; set; } // settable so a live rename updates the spoken cycle name

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

        // Only run the systems that belong to the live context — so navmesh-bound in-area systems stay off
        // the world map and world-map systems stay off in areas.
        private static bool Applies(OverlayScope sys) => sys == OverlayScope.Both || sys == OverlayManager.CurrentScope;
        private static bool InAreaNow => OverlayManager.CurrentScope == OverlayScope.InArea;

        public void OnEnter()
        {
            foreach (var s in _systems) if (Applies(s.Scope)) s.OnEnter(this);
        }
        public void OnExit()
        {
            foreach (var s in _systems) if (Applies(s.Scope)) s.OnExit(this);
            TerrainSounds.Teardown();
        }

        /// <summary>One frame of sensing. The cursor has already moved this frame (OverlayManager ticks it
        /// first), so the systems read the fresh position. Each system decides what, if anything, to
        /// suppress; the overlay's master gate (OverlayManager.InExploration) is context-only, not control.</summary>
        public void Tick(float dt)
        {
            foreach (var s in _systems) if (Applies(s.Scope)) s.Tick(dt, this);
            if (InAreaNow) TerrainSounds.Tick(dt); else TerrainSounds.Teardown();
        }

        // ---- the readout (the lens half of the ONE pipeline; the cursor speaks it) ----

        /// <summary>Gather every system's announcements for the requested context and trigger, order
        /// them (position, effect, path, contents), drop exact repeats, and compose one line — or null
        /// when nothing applies (a Settle with nothing new stays silent).</summary>
        public string Compose(OverlayContext ctx)
        {
            var parts = new List<OverlayAnnouncement>();
            foreach (var s in _systems)
                if (Applies(s.Scope))
                    foreach (var a in s.Announce(ctx))
                        if (a != null && a.Context == ctx.Want && a.Text != null) parts.Add(a);
            if (parts.Count == 0) return null;
            parts.Sort((a, b) => a.Order.CompareTo(b.Order)); // List.Sort is unstable; equal orders keep system order below
            var seen = new HashSet<string>();
            var texts = new List<string>();
            foreach (var a in parts)
            {
                var t = a.Text.Resolve();
                if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
                texts.Add(t);
            }
            return texts.Count == 0 ? null : string.Join("; ", texts.ToArray());
        }
    }
}
