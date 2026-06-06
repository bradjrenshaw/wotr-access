using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Base for the sound-producing systems (sonar, wall tones, fog/object cues) — they all share an
    /// <c>enabled</c> toggle (from the base) and a per-system <c>volume</c> (0–100%). Effective loudness is
    /// the global master (the Audio settings tab) times this; <see cref="Volume"/> exposes the per-system
    /// fraction. Subclasses add their own tunables in <see cref="RegisterAudioSettings"/>.
    /// </summary>
    internal abstract class AudioSystem : OverlaySystem
    {
        public override void RegisterSettings(CategorySetting cat)
        {
            cat.Add(new IntSetting("volume", "Volume", 100, 0, 100, 5, "overlay.volume"));
            RegisterAudioSettings(cat);
        }

        protected virtual void RegisterAudioSettings(CategorySetting cat) { }

        /// <summary>This system's own volume as a 0..1 fraction.</summary>
        protected float Volume => Int("volume", 100) / 100f;

        /// <summary>Per-system volume scaled by the global master — what the engine should actually use.</summary>
        protected float EffectiveVolume => Volume * OverlayAudio.Master;
    }
}
