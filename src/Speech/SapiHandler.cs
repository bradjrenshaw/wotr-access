using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Windows SAPI 5 driven directly over COM through <see cref="ComDispatch"/> (manual IDispatch —
    /// see that class for why: Unity's Mono implements neither System.Speech's registry internals nor
    /// managed COM activation). Rate / volume / voice settings; the no-screen-reader fallback.
    ///
    /// Also the audio RENDERER for world-positioned speech: <see cref="RenderToAudio"/> synthesizes to
    /// an <c>SpMemoryStream</c> on a second, independent SpVoice (so rendering never disturbs live
    /// speech — and it works even when the active handler is Prism/NVDA).
    /// </summary>
    public class SapiHandler : ISpeechHandler
    {
        // SpeechVoiceSpeakFlags
        private const int SVSFlagsAsync = 1;
        private const int SVSFPurgeBeforeSpeak = 2;
        // SpeechAudioFormatType for the render stream
        private const int SAFT22kHz16BitMono = 22;
        private const int RenderSampleRate = 22050;

        private ComDispatch _voice;        // live speech
        private ComDispatch _renderVoice;  // render-to-memory (independent of live speech)
        private CategorySetting _settings;
        private IntSetting _rate;
        private IntSetting _volume;
        private ChoiceSetting _voiceSetting;

        public string Key => "sapi";
        public string Label => "SAPI";
        public string LocalizationKey => "speech.sapi";

        public CategorySetting GetSettings()
        {
            if (_settings != null) return _settings;

            _settings = new CategorySetting(Key, Label, localizationKey: "speech.sapi");
            _rate = new IntSetting("rate", "Rate", 2, -10, 10, 1, "speech.sapi.rate");
            _volume = new IntSetting("volume", "Volume", 100, 0, 100, 5, "speech.sapi.volume");

            var voices = new List<Choice>();
            try
            {
                foreach (var name in VoiceNames()) voices.Add(new Choice(name, name));
            }
            catch (Exception ex)
            {
                Main.Log?.Warning("[speech] Failed to enumerate SAPI voices: " + ex.Message);
            }
            if (voices.Count == 0) voices.Add(new Choice("default", "Default"));
            _voiceSetting = new ChoiceSetting("voice", "Voice", voices, voices[0].Id, "speech.sapi.voice");

            _settings.Add(_rate);
            _settings.Add(_volume);
            _settings.Add(_voiceSetting);

            _rate.Changed += v => { try { _voice?.Set("Rate", v); } catch { } };
            _volume.Changed += v => { try { _voice?.Set("Volume", v); } catch { } };
            _voiceSetting.Changed += v =>
            {
                try { if (_voice != null) SelectVoice(_voice, v); }
                catch (Exception ex) { Main.Log?.Error("[speech] Voice select failed: " + ex.Message); }
            };

            return _settings;
        }

        public bool Detect()
        {
            try
            {
                var probe = ComDispatch.Create("SAPI.SpVoice");
                if (probe == null) return false;
                probe.Dispose();
                return true;
            }
            catch { return false; }
        }

        public bool Load()
        {
            try
            {
                _voice = ComDispatch.Create("SAPI.SpVoice");
                if (_voice == null)
                {
                    Main.Log?.Error("[speech] SapiHandler: SAPI.SpVoice not available.");
                    return false;
                }
                Apply(_voice);
                Main.Log?.Log("[speech] SAPI handler loaded (manual COM).");
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler failed to load: " + ex);
                _voice?.Dispose();
                _voice = null;
                return false;
            }
        }

        public void Unload()
        {
            _voice?.Dispose();
            _voice = null;
            _renderVoice?.Dispose();
            _renderVoice = null;
        }

        public bool Speak(string text, bool interrupt = false)
        {
            if (_voice == null) return false;
            try
            {
                _voice.Call("Speak", text, interrupt ? SVSFlagsAsync | SVSFPurgeBeforeSpeak : SVSFlagsAsync);
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt = false) => Speak(text, interrupt);

        public bool Silence()
        {
            if (_voice == null) return false;
            try
            {
                // The standard SAPI "stop": purge the queue with an empty async utterance.
                _voice.Call("Speak", string.Empty, SVSFlagsAsync | SVSFPurgeBeforeSpeak);
                return true;
            }
            catch { return false; }
        }

        // ---- render-to-audio (for world-positioned speech) ----

        public bool SupportsAudioRender => true;

        public SpeechAudio RenderToAudio(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            ComDispatch stream = null, format = null;
            try
            {
                // Lazily create the render voice — independent of Load(), so rendering works even when
                // SAPI isn't the active live handler.
                if (_renderVoice == null)
                {
                    _renderVoice = ComDispatch.Create("SAPI.SpVoice");
                    if (_renderVoice == null) return null;
                }
                Apply(_renderVoice);

                // Fresh memory stream per utterance, pinned to a known PCM format. Without
                // AllowAudioOutputFormatChangesOnNextSet=false SAPI rewrites the stream's format to the
                // engine default on assignment, and we'd mis-read the samples.
                stream = ComDispatch.Create("SAPI.SpMemoryStream");
                if (stream == null) return null;
                format = (ComDispatch)stream.Get("Format");
                format.Set("Type", SAFT22kHz16BitMono);
                _renderVoice.Set("AllowAudioOutputFormatChangesOnNextSet", false);
                _renderVoice.SetRef("AudioOutputStream", stream);

                _renderVoice.Call("Speak", text, 0); // synchronous — the data is complete on return

                var data = stream.Call("GetData") as byte[];
                if (data == null || data.Length == 0) return null;
                return new SpeechAudio
                {
                    Pcm = data,
                    SampleRate = RenderSampleRate,
                    Channels = 1,
                    BitsPerSample = 16,
                };
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler.RenderToAudio failed: " + ex.Message);
                return null;
            }
            finally
            {
                format?.Dispose();
                stream?.Dispose();
            }
        }

        // ---- shared voice plumbing ----

        /// <summary>Apply the user's rate/volume/voice settings to a SpVoice instance.</summary>
        private void Apply(ComDispatch voice)
        {
            try { voice.Set("Rate", _rate != null ? _rate.Get() : 2); } catch { }
            try { voice.Set("Volume", _volume != null ? _volume.Get() : 100); } catch { }
            var name = _voiceSetting?.Current?.Id;
            if (!string.IsNullOrEmpty(name) && name != "default")
            {
                try { SelectVoice(voice, name); } catch { }
            }
        }

        private static IEnumerable<string> VoiceNames()
        {
            var probe = ComDispatch.Create("SAPI.SpVoice");
            if (probe == null) yield break;
            try
            {
                var tokens = (ComDispatch)probe.Call("GetVoices", string.Empty, string.Empty);
                if (tokens == null) yield break;
                try
                {
                    int count = Convert.ToInt32(tokens.Get("Count"));
                    for (int i = 0; i < count; i++)
                    {
                        string name = null;
                        var token = (ComDispatch)tokens.Call("Item", i);
                        if (token != null)
                        {
                            try { name = token.Call("GetDescription", 0) as string; }
                            finally { token.Dispose(); }
                        }
                        if (!string.IsNullOrEmpty(name)) yield return name;
                    }
                }
                finally { tokens.Dispose(); }
            }
            finally { probe.Dispose(); }
        }

        private static void SelectVoice(ComDispatch voice, string description)
        {
            var tokens = (ComDispatch)voice.Call("GetVoices", string.Empty, string.Empty);
            if (tokens == null) return;
            try
            {
                int count = Convert.ToInt32(tokens.Get("Count"));
                for (int i = 0; i < count; i++)
                {
                    var token = (ComDispatch)tokens.Call("Item", i);
                    if (token == null) continue;
                    if (token.Call("GetDescription", 0) as string == description)
                    {
                        voice.SetRef("Voice", token); // putref — SAPI object-valued property
                        token.Dispose();
                        return;
                    }
                    token.Dispose();
                }
            }
            finally { tokens.Dispose(); }
        }
    }
}
