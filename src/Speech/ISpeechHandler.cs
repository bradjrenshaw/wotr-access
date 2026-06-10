using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// One speech backend (ported from SayTheSpire2). Handlers self-describe (key + label + optional
    /// settings subtree), report whether they'd work on this machine (<see cref="Detect"/>), and speak.
    /// <see cref="Output"/> drives speech AND braille where the backend supports it; <see cref="Speak"/>
    /// is voice only. The <see cref="SpeechManager"/> owns selection/fallback.
    /// </summary>
    public interface ISpeechHandler
    {
        string Key { get; }
        string Label { get; }
        /// <summary>Localization key for the handler's display label ("" = use the raw label).</summary>
        string LocalizationKey { get; }
        /// <summary>The handler's own settings subtree (nested under the Speech category), or null.</summary>
        CategorySetting GetSettings();
        bool Detect();
        bool Load();
        void Unload();
        bool Speak(string text, bool interrupt = false);
        bool Output(string text, bool interrupt = false);
        bool Silence();

        /// <summary>Whether this handler can render speech to PCM (for world-positioned playback
        /// through the spatial audio pipeline) instead of speaking it immediately.</summary>
        bool SupportsAudioRender { get; }

        /// <summary>Render <paramref name="text"/> to PCM with the handler's current voice settings.
        /// Null when unsupported or failed. Must be independent of the live speech path (rendering
        /// must never cut off or get cut off by spoken announcements).</summary>
        SpeechAudio RenderToAudio(string text);
    }

    /// <summary>A rendered utterance: raw PCM + its format (16-bit signed little-endian).</summary>
    public sealed class SpeechAudio
    {
        public byte[] Pcm;
        public int SampleRate = 22050;
        public int Channels = 1;
        public int BitsPerSample = 16;
    }
}
