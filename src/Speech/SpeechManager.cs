using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Owns the speech handlers (ported from SayTheSpire2): an ordered list (Prism → SAPI → Clipboard),
    /// a "handler" choice setting (auto = first that detects and loads), each handler's own settings
    /// subtree nested under the Speech category, and hot-swap on change with a spoken confirmation.
    /// <see cref="Tts"/> is the call-site facade over this.
    /// </summary>
    public static class SpeechManager
    {
        private static ISpeechHandler _activeHandler;
        private static bool _initialized;
        private static ChoiceSetting _handlerSetting;

        public static readonly IReadOnlyList<ISpeechHandler> Handlers = new List<ISpeechHandler>
        {
            new PrismHandler(),
            new SapiHandler(),
            new ClipboardHandler(),
        };

        /// <summary>Build the Speech settings tree: the handler dropdown, then each handler's subtree.</summary>
        public static void RegisterSettings(CategorySetting speechCategory)
        {
            var handlerChoices = new List<Choice> { new Choice("auto", "Auto", "speech.auto") };
            foreach (var handler in Handlers)
                handlerChoices.Add(new Choice(handler.Key, handler.Label, handler.LocalizationKey));
            _handlerSetting = new ChoiceSetting("handler", "Speech handler", handlerChoices, "auto", "speech.handler");
            speechCategory.Add(_handlerSetting);
            _handlerSetting.Changed += OnHandlerChanged;

            foreach (var handler in Handlers)
            {
                var handlerSettings = handler.GetSettings();
                if (handlerSettings != null) speechCategory.Add(handlerSettings);
            }
        }

        /// <summary>Activate the configured handler. Called AFTER settings load (the choice persists).</summary>
        public static void Initialize()
        {
            ActivateHandler(_handlerSetting?.Current?.Id ?? "auto");
        }

        public static bool Ready => _initialized && _activeHandler != null;

        public static void Speak(string text, bool interrupt = false)
        {
            if (Ready) _activeHandler.Speak(text, interrupt);
        }

        /// <summary>Speech AND braille where the backend supports it — the default output path.</summary>
        public static void Output(string text, bool interrupt = false)
        {
            if (Ready) _activeHandler.Output(text, interrupt);
        }

        public static void Silence()
        {
            if (Ready) _activeHandler.Silence();
        }

        /// <summary>
        /// Render text to PCM for world-positioned playback (damage numbers at the enemy's position,
        /// etc.): the active handler if it can render, else the first handler that can (handlers'
        /// render paths are self-sufficient — they don't require being the live handler). Null if no
        /// handler can render.
        /// </summary>
        public static SpeechAudio RenderToAudio(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (Ready && _activeHandler.SupportsAudioRender)
                return _activeHandler.RenderToAudio(text);
            foreach (var handler in Handlers)
                if (handler.SupportsAudioRender)
                    return handler.RenderToAudio(text);
            return null;
        }

        private static void OnHandlerChanged(string key)
        {
            if (!_initialized) return; // pre-init writes are just the settings file loading
            ActivateHandler(key);
            if (_activeHandler != null)
                Output(Message.Localized("ui", "speech.handler_changed", new { handler = _activeHandler.Label }).Resolve());
        }

        private static void ActivateHandler(string key)
        {
            _activeHandler?.Unload();
            _activeHandler = null;
            _initialized = false;

            if (key == "auto")
            {
                foreach (var handler in Handlers)
                {
                    try
                    {
                        Main.Log?.Log("[speech] Trying handler: " + handler.Key);
                        if (handler.Detect() && handler.Load())
                        {
                            _activeHandler = handler;
                            _initialized = true;
                            Main.Log?.Log("[speech] Active handler: " + handler.Key);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Main.Log?.Error("[speech] Handler " + handler.Key + " failed: " + ex);
                    }
                }
                Main.Log?.Error("[speech] No speech handler could be loaded!");
                return;
            }

            // A specific handler; fall back to auto if it can't load.
            ISpeechHandler chosen = null;
            foreach (var handler in Handlers)
                if (handler.Key == key) { chosen = handler; break; }
            if (chosen == null)
            {
                Main.Log?.Error("[speech] Unknown speech handler: " + key);
                ActivateHandler("auto");
                return;
            }
            try
            {
                if (chosen.Load())
                {
                    _activeHandler = chosen;
                    _initialized = true;
                    Main.Log?.Log("[speech] Active handler: " + chosen.Key);
                }
                else
                {
                    Main.Log?.Error("[speech] Handler " + key + " failed to load; falling back to auto");
                    ActivateHandler("auto");
                }
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] Handler " + key + " failed: " + ex + "; falling back to auto");
                ActivateHandler("auto");
            }
        }
    }
}
