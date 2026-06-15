using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A named, reusable way to speak — a handler choice plus that handler's params (SAPI rate/volume/
    /// voice, Prism backend), held in a settings subtree. The DEFAULT config is the root Speech settings
    /// (drives all UI/announcement/dialogue speech); the user can add more (advanced) for the events
    /// system to speak specific things through — e.g. enemy damage in a different voice. An event "speaks
    /// through" a config: the config resolves its handler (loading on demand), applies its params, and
    /// speaks or renders.
    /// </summary>
    public sealed class SpeechConfig
    {
        /// <summary>The config's settings subtree: a "handler" choice + one subnode per handler.</summary>
        public CategorySetting Tree { get; }

        public SpeechConfig(CategorySetting tree) { Tree = tree; }

        /// <summary>The chosen handler key ("auto" = best available), from the config's handler dropdown.</summary>
        public string HandlerKey => Tree?.Get<ChoiceSetting>("handler")?.Current?.Id ?? "auto";

        // The resolved, loaded handler for this config (load-on-demand + auto/fallback via SpeechManager).
        private ISpeechHandler Handler => SpeechManager.ResolveHandler(HandlerKey);

        // This handler's own params subtree within the config (null for paramless handlers like clipboard).
        private CategorySetting Params(ISpeechHandler h) => h != null ? Tree?.Get<CategorySetting>(h.Key) : null;

        /// <summary>Can speech through this config be positioned in the world (handler renders to PCM)?
        /// SAPI yes, Prism/clipboard no. The "use positional" CHOICE is a separate, event-side option.</summary>
        public bool SupportsPositional => Handler?.SupportsAudioRender ?? false;

        public bool Speak(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Speak(text, interrupt, Params(h));
        }

        public bool Output(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Output(text, interrupt, Params(h));
        }

        public bool Silence()
        {
            var h = Handler;
            return h != null && h.Silence();
        }

        /// <summary>Render through this config's handler for world-positioned playback, applying its
        /// voice. Falls back to any render-capable handler when this config's can't render.</summary>
        public SpeechAudio RenderToAudio(string text)
        {
            var h = Handler;
            if (h != null && h.SupportsAudioRender) return h.RenderToAudio(text, Params(h));
            return SpeechManager.RenderToAudioFallback(text);
        }
    }
}
