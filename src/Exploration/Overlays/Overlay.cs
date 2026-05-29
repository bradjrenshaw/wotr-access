using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// One way of presenting the surrounding area to a blind player. Overlays are interchangeable lenses
    /// over the same world model (entities, navmesh, markers); the player swaps between them to suit what
    /// they're doing. The <see cref="OverlayManager"/> owns which one is active and forwards input to it.
    /// Subclasses override only the verbs they use. First overlay: <see cref="VirtualTileView"/>.
    /// </summary>
    internal abstract class Overlay
    {
        public abstract string Name { get; }

        /// <summary>Becoming the active overlay (e.g. plant the cursor on the player and announce).</summary>
        public virtual void OnEnter() { }
        public virtual void OnExit() { }

        /// <summary>Per-frame update while this overlay is the selected one (for continuous movement,
        /// audio, etc.). Called every frame regardless of focus/screen — the overlay self-gates on
        /// <see cref="OverlayManager.Active"/>. <paramref name="dt"/> is unscaled delta time.</summary>
        public virtual void Tick(float dt) { }

        /// <summary>Arrow-key movement of the overlay's cursor/focus.</summary>
        public virtual void Move(NavDirection dir) { }

        /// <summary>Follow a surface to the level below (-1) or above (+1) — for stacked levels.</summary>
        public virtual void VerticalFollow(int dir) { }

        /// <summary>Reset the overlay's focus to the player.</summary>
        public virtual void Recenter() { }

        /// <summary>Re-announce the current focus without moving.</summary>
        public virtual void AnnounceCurrent() { }
    }
}
