using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Owns the speech handlers (Prism → SAPI → Clipboard) and resolves them LOAD-ON-DEMAND so any number
    /// of <see cref="SpeechConfig"/>s can speak through different handlers at once (they're independent
    /// native engines). The DEFAULT config is the root Speech settings and drives all UI/announcement/
    /// dialogue speech (<see cref="Speak"/>/<see cref="Output"/>); the events system speaks specific things
    /// through additional configs. The same schema (handler choice + each handler's params) is built into
    /// every config via <see cref="BuildConfigSchema"/>. <see cref="Tts"/> is the call-site facade.
    /// </summary>
    public static class SpeechManager
    {
        /// <summary>Sentinel value for an additional config's setting that follows the default config (the
        /// choice id; the loc key is "choice.inherit"). The default config never uses it — it's the base.</summary>
        internal const string Inherit = "inherit";

        private static bool _initialized;
        private static ChoiceSetting _handlerSetting;          // the DEFAULT config's handler choice
        private static readonly HashSet<ISpeechHandler> _loaded = new HashSet<ISpeechHandler>();

        /// <summary>The default speech config — the root Speech settings; everything speaks through it.</summary>
        public static SpeechConfig Default { get; private set; }

        public static readonly IReadOnlyList<ISpeechHandler> Handlers = new List<ISpeechHandler>
        {
            new PrismHandler(),
            new SapiHandler(),
            new ClipboardHandler(),
        };

        /// <summary>Build the DEFAULT config into the Speech category (handler dropdown + each handler's
        /// params), and adopt it as <see cref="Default"/>.</summary>
        public static void RegisterSettings(CategorySetting speechCategory)
        {
            BuildConfigSchema(speechCategory);
            _handlerSetting = speechCategory.Get<ChoiceSetting>("handler");
            if (_handlerSetting != null) _handlerSetting.Changed += OnDefaultHandlerChanged;
            Default = new SpeechConfig(speechCategory);
        }

        /// <summary>The schema every speech config shares: a "handler" choice + one subnode per handler
        /// holding that handler's params. The default config (<paramref name="inheritFrom"/> null) gets plain
        /// settings; an additional config gets INHERIT-aware ones — every setting can follow the default
        /// config (<see cref="Inherit"/>) until overridden — wired to <paramref name="inheritFrom"/>'s
        /// matching subtree as the source. Resolution happens in <see cref="SpeechConfig"/>, so handlers
        /// stay inherit-agnostic.</summary>
        public static void BuildConfigSchema(CategorySetting into, CategorySetting inheritFrom = null)
        {
            bool inherit = inheritFrom != null;

            var handlerChoices = new List<Choice>();
            if (inherit) handlerChoices.Add(new Choice(Inherit, "Inherit", "choice.inherit"));
            handlerChoices.Add(new Choice("auto", "Auto", "speech.auto"));
            foreach (var handler in Handlers)
                handlerChoices.Add(new Choice(handler.Key, handler.Label, handler.LocalizationKey));
            into.Add(new ChoiceSetting("handler", "Speech handler", handlerChoices,
                inherit ? Inherit : "auto", "speech.handler"));

            foreach (var handler in Handlers)
            {
                var sub = new CategorySetting(handler.Key, handler.Label, localizationKey: "speech." + handler.Key);
                if (inherit)
                    BuildInheritParams(sub, handler, inheritFrom.Get<CategorySetting>(handler.Key));
                else
                    handler.BuildSettings(sub);
                if (sub.Children.Count > 0) into.Add(sub);
            }
        }

        // Translate a handler's normal param schema into inherit-aware settings: each int becomes a
        // NullableIntSetting that follows the default config's same int; each choice gains an "Inherit"
        // option (default). The handler builds its plain schema into a scratch node (it knows nothing of
        // inherit), which we read to mirror keys/ranges/choices. defaultSub = the same handler's params in
        // the default config (the inheritance source; may be null if it has none).
        private static void BuildInheritParams(CategorySetting into, ISpeechHandler handler, CategorySetting defaultSub)
        {
            var scratch = new CategorySetting(handler.Key, handler.Label);
            handler.BuildSettings(scratch);
            foreach (var child in scratch.Children)
            {
                switch (child)
                {
                    case IntSetting i:
                        into.Add(new NullableIntSetting(i.Key, i.Label, defaultSub?.Get<IntSetting>(i.Key),
                            i.Min, i.Max, i.Step, i.LocalizationKey));
                        break;
                    case ChoiceSetting c:
                        var choices = new List<Choice> { new Choice(Inherit, "Inherit", "choice.inherit") };
                        foreach (var ch in c.Choices) choices.Add(ch);
                        into.Add(new ChoiceSetting(c.Key, c.Label, choices, Inherit, c.LocalizationKey));
                        break;
                    default:
                        into.Add(child); // any other type passes through (none today)
                        break;
                }
            }
        }

        /// <summary>Activate the default config's handler now (after settings load) so the first utterance
        /// is instant.</summary>
        public static void Initialize()
        {
            _initialized = true;
            ResolveHandler(Default?.HandlerKey ?? "auto");
        }

        public static bool Ready => _initialized && Default != null;

        // ---- the default-config speak/render API (UI / announcements / dialogue) ----

        public static void Speak(string text, bool interrupt = false) { if (Ready) Default.Speak(text, interrupt); }
        public static void Output(string text, bool interrupt = false) { if (Ready) Default.Output(text, interrupt); }
        public static void Silence() { if (Ready) Default.Silence(); }

        /// <summary>Render text to PCM for world-positioned playback through the default config.</summary>
        public static SpeechAudio RenderToAudio(string text)
            => Ready ? Default.RenderToAudio(text) : RenderToAudioFallback(text);

        /// <summary>Render via the first render-capable handler, applying the DEFAULT config's params for
        /// it — used when a config's chosen handler can't render (e.g. Prism). Independent of the live path.</summary>
        public static SpeechAudio RenderToAudioFallback(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var handler in Handlers)
                if (handler.SupportsAudioRender && EnsureLoaded(handler))
                    return handler.RenderToAudio(text, Default?.Tree?.Get<CategorySetting>(handler.Key));
            return null;
        }

        // ---- load-on-demand handler resolution (shared by every config) ----

        /// <summary>The loaded handler for a config's "handler" choice: "auto" = the first that detects +
        /// loads; a specific key = that handler (falling back to auto if it can't load). Null if none load.</summary>
        public static ISpeechHandler ResolveHandler(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "auto")
            {
                foreach (var handler in Handlers)
                    if (EnsureLoaded(handler)) return handler;
                Main.Log?.Error("[speech] No speech handler could be loaded!");
                return null;
            }

            foreach (var handler in Handlers)
                if (handler.Key == key)
                    return EnsureLoaded(handler) ? handler : ResolveHandler("auto");

            Main.Log?.Error("[speech] Unknown speech handler: " + key);
            return ResolveHandler("auto");
        }

        private static bool EnsureLoaded(ISpeechHandler handler)
        {
            if (_loaded.Contains(handler)) return true;
            try
            {
                if (handler.Detect() && handler.Load())
                {
                    _loaded.Add(handler);
                    Main.Log?.Log("[speech] handler loaded: " + handler.Key);
                    return true;
                }
            }
            catch (Exception ex) { Main.Log?.Error("[speech] Handler " + handler.Key + " failed: " + ex.Message); }
            return false;
        }

        private static void OnDefaultHandlerChanged(string key)
        {
            if (!_initialized) return; // pre-init writes are just the settings file loading
            var handler = ResolveHandler(key);
            if (handler != null)
                Default.Output(Message.Localized("ui", "speech.handler_changed", new { handler = handler.Label }).Resolve());
        }
    }
}
