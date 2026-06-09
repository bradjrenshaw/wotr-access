using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A pure provider attached to an <see cref="Overlay"/> — it queries the world relative to the cursor
    /// and exposes <see cref="OverlayAnnouncement"/>s and/or makes sound in <see cref="Tick"/>; it NEVER
    /// moves the cursor or owns movement keys (that's <see cref="MovementMode"/>), which is what lets a
    /// tiled-space system and a continuous-space system coexist on one overlay. Exactly one of each
    /// concrete type per overlay, so siblings can be looked up by type.
    ///
    /// Each system is data-driven: it declares its tunables in <see cref="RegisterSettings"/> and reads them
    /// LIVE through the bound category (via <see cref="Bool"/>/<see cref="Int"/>/<see cref="ChoiceId"/>), so
    /// editing a setting takes effect immediately. The universal <c>enabled</c> toggle is added by the
    /// registry; a disabled system self-gates (no announcements, no Tick work).
    /// </summary>
    internal abstract class OverlaySystem
    {
        public abstract string Name { get; }
        public abstract string Key { get; } // settings-path segment, e.g. "grid"

        /// <summary>The bound per-overlay settings category (for subclasses with nested setting groups).</summary>
        protected CategorySetting Settings { get; private set; }
        public void Bind(CategorySetting settings) => Settings = settings;

        /// <summary>Add this system's tunables to its settings category (the <c>enabled</c> toggle and the
        /// audio <c>volume</c> are added for you — see the registry / <see cref="AudioSystem"/>).</summary>
        public virtual void RegisterSettings(CategorySetting cat) { }

        public bool Enabled => Bool("enabled", true);

        protected bool Bool(string key, bool fallback) => Settings?.Get<BoolSetting>(key)?.Get() ?? fallback;
        protected int Int(string key, int fallback) => Settings?.Get<IntSetting>(key)?.Get() ?? fallback;
        protected string ChoiceId(string key, string fallback) => Settings?.Get<ChoiceSetting>(key)?.Current?.Id ?? fallback;

        public virtual void OnEnter(Overlay overlay) { }
        public virtual void OnExit(Overlay overlay) { }

        /// <summary>Per-frame work while an overlay is selected; self-gates on
        /// <see cref="OverlayManager.Active"/> and <see cref="Enabled"/>.</summary>
        public virtual void Tick(float dt, Overlay overlay) { }

        /// <summary>The announcements this system contributes (each tagged with its
        /// <see cref="AnnouncementContext"/>); empty if disabled or none.</summary>
        public virtual IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            yield break;
        }
    }
}
