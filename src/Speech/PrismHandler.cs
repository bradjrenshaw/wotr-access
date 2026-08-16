using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Speech handler routing through Prism (https://github.com/ethindp/prism) — a unified native
    /// abstraction over screen-reader and TTS backends (NVDA, JAWS, SAPI, OneCore, …). The primary
    /// handler. Param-driven: a config's "backend" choice is applied on change (rebinding only when it
    /// actually differs, since the feature query / rebind cost a native round-trip). Screen-reader
    /// passthrough, so it cannot render to PCM (no positional speech) — that's the SAPI handler's job.
    /// </summary>
    public class PrismHandler : ISpeechHandler
    {
        private const string AutoBackend = "auto";

        private IntPtr _ctx = IntPtr.Zero;
        private IntPtr _backend = IntPtr.Zero;
        private PrismNative.BackendFeatures _backendFeatures;
        private string _currentBackend = AutoBackend; // last backend applied from a config (apply-on-change)
        private static List<string> _backendNames;    // enumerated once (the registry probe is expensive)

        // One LIVE backend per preference id, so configs that alternate (default = a screen reader,
        // events = OneCore) COEXIST. The old single-handle design stopped AND FREED the other
        // backend on every switch — the moment the screen-reader channel spoke, OneCore was
        // destroyed mid-utterance ("SR speech interrupts my combat events" — tester repro), and
        // capability checks answered for whichever backend happened to be current. Per-slot applied
        // params persist across switches (a switch is now a lookup, not a teardown), and Silence
        // stops only the CURRENT slot — an interrupt on one channel never cuts the other off.
        private sealed class BackendSlot
        {
            public IntPtr Handle;
            public PrismNative.BackendFeatures Features;
            public int AppliedRate = -1, AppliedVolume = -1;
            public string AppliedVoice;
        }
        private readonly Dictionary<string, BackendSlot> _slots = new Dictionary<string, BackendSlot>();
        private BackendSlot _current;

        // The slot for a preference id — acquired once, cached forever (including acquisition
        // FAILURES as zero-handle slots, so a broken choice isn't re-attempted per utterance).
        // Null (uncached) only when the handler isn't loaded yet.
        private BackendSlot EnsureSlot(string pref)
        {
            if (_ctx == IntPtr.Zero) return null;
            BackendSlot slot;
            if (_slots.TryGetValue(pref, out slot)) return slot;
            var handle = ResolveBackend(pref);
            slot = new BackendSlot
            {
                Handle = handle,
                Features = handle != IntPtr.Zero
                    ? (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(handle) : 0,
            };
            _slots[pref] = slot;
            return slot;
        }

        public string Key => "prism";
        public string Label => "Prism";
        public string LocalizationKey => "speech.prism";

        // The backend itself is fixed by the OUTPUT the config picked (SpeechConfig synthesizes the
        // "backend" param per speak; the old per-config Backend dropdown is gone — Prism is an engine,
        // not a choice). These are the PER-CONFIG params applied at speak time, each gated on the
        // bound backend's feature bits — OneCore honours all three; screen readers own their voice and
        // rate and don't advertise the knobs, so the requests are skipped there. Defaults are the
        // backends' own defaults (rate 50 = OneCore 1.0x, volume 100, voice untouched), so an
        // untouched config sounds exactly as before.
        public void BuildSettings(CategorySetting into)
        {
            into.Add(new IntSetting("rate", "Rate", 50, 0, 100, 5, "speech.prism.rate"));
            into.Add(new IntSetting("volume", "Volume", 100, 0, 100, 5, "speech.prism.volume"));
            into.Add(new ChoiceSetting("voice", "Voice", VoiceChoices(), "default", "speech.prism.voice"));
        }

        // All voices of every runtime-supported, voice-selectable Prism backend (OneCore in practice —
        // SAPI-ish backends are excluded to match the outputs list; screen readers don't expose
        // voices), deduped by name. "Default" = leave the backend's own voice untouched. Cached: the
        // probe initializes real synth engines.
        private static List<Settings.Choice> _voiceChoices;
        private static List<Settings.Choice> VoiceChoices()
        {
            if (_voiceChoices != null) return _voiceChoices;
            var choices = new List<Settings.Choice> { new Settings.Choice("default", "Default", "speech.voice.default") };
            try
            {
                var ctx = PrismNative.Init(IntPtr.Zero);
                if (ctx != IntPtr.Zero)
                {
                    try
                    {
                        int count = (int)PrismNative.RegistryCount(ctx).ToUInt64();
                        for (int i = 0; i < count; i++)
                        {
                            var id = PrismNative.RegistryIdAt(ctx, (UIntPtr)(uint)i);
                            var name = PrismNative.RegistryName(ctx, id);
                            if (string.IsNullOrEmpty(name)
                                || name.IndexOf("sapi", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            var backend = PrismNative.RegistryCreate(ctx, id);
                            if (backend == IntPtr.Zero) continue;
                            try
                            {
                                var features = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(backend);
                                if ((features & PrismNative.BackendFeatures.SupportedAtRuntime) == 0
                                    || (features & PrismNative.BackendFeatures.SupportsSetVoice) == 0
                                    || (features & PrismNative.BackendFeatures.SupportsCountVoices) == 0) continue;
                                var initErr = PrismNative.BackendInitialize(backend);
                                if (initErr != PrismNative.PrismError.Ok
                                    && initErr != PrismNative.PrismError.AlreadyInitialized) continue;
                                if (PrismNative.BackendCountVoices(backend, out var vc) != PrismNative.PrismError.Ok) continue;
                                int voices = (int)vc.ToUInt64();
                                for (int v = 0; v < voices; v++)
                                {
                                    var vn = PrismNative.BackendGetVoiceName(backend, (ulong)v);
                                    if (string.IsNullOrEmpty(vn)) continue;
                                    bool dup = false;
                                    foreach (var c in choices) if (c.Id == vn) { dup = true; break; }
                                    if (!dup) choices.Add(new Settings.Choice(vn, vn)); // voice product names — not translated
                                }
                            }
                            finally { PrismNative.BackendFree(backend); }
                        }
                    }
                    finally { PrismNative.Shutdown(ctx); }
                }
            }
            catch (DllNotFoundException) { /* prism.dll missing — voice list stays Default-only */ }
            catch (Exception ex) { Main.Log?.Warning("[speech] Prism voice enumeration failed: " + ex.Message); }
            return _voiceChoices = choices;
        }

        /// <summary>Enumerate prism's registry once and keep only backends whose engine is actually
        /// available on this machine (SupportedAtRuntime filters out the obviously-irrelevant, e.g.
        /// JAWS with no JAWS). Feeds the output list (<see cref="SpeechOutputs"/>).</summary>
        public static IReadOnlyList<string> ProbeBackendNames()
        {
            if (_backendNames != null) return _backendNames;
            var names = new List<string>();
            try
            {
                var probeCtx = PrismNative.Init(IntPtr.Zero);
                if (probeCtx != IntPtr.Zero)
                {
                    try
                    {
                        var count = (int)PrismNative.RegistryCount(probeCtx).ToUInt64();
                        for (int i = 0; i < count; i++)
                        {
                            var id = PrismNative.RegistryIdAt(probeCtx, (UIntPtr)(uint)i);
                            var name = PrismNative.RegistryName(probeCtx, id);
                            if (string.IsNullOrEmpty(name)) continue;
                            var backend = PrismNative.RegistryCreate(probeCtx, id);
                            if (backend == IntPtr.Zero) continue;
                            try
                            {
                                var features = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(backend);
                                if ((features & PrismNative.BackendFeatures.SupportedAtRuntime) != 0)
                                    names.Add(name); // backend names are product names — not translated
                            }
                            finally { PrismNative.BackendFree(backend); }
                        }
                    }
                    finally { PrismNative.Shutdown(probeCtx); }
                }
            }
            catch (DllNotFoundException) { /* prism.dll missing — no screen-reader outputs */ }
            catch (Exception ex) { Main.Log?.Warning("[speech] Prism backend enumeration failed: " + ex.Message); }
            return _backendNames = names;
        }

        public bool Detect()
        {
            try
            {
                var ctx = PrismNative.Init(IntPtr.Zero);
                if (ctx == IntPtr.Zero) { Main.Log?.Log("[speech] Prism: prism_init returned null (dll loaded but init failed)."); return false; }
                try
                {
                    var backend = PrismNative.RegistryCreateBest(ctx);
                    if (backend == IntPtr.Zero) { Main.Log?.Log("[speech] Prism: no usable backend on this machine."); return false; }
                    PrismNative.BackendFree(backend);
                    return true;
                }
                finally { PrismNative.Shutdown(ctx); }
            }
            catch (DllNotFoundException)
            {
                Main.Log?.Log("[speech] Prism: prism.dll not found next to Wrath.exe (or a dependency is missing — e.g. the VC++ runtime).");
                return false;
            }
            catch (Exception ex)
            {
                Main.Log?.Log("[speech] PrismHandler.Detect failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public bool Load()
        {
            try
            {
                _ctx = PrismNative.Init(IntPtr.Zero);
                if (_ctx == IntPtr.Zero)
                {
                    Main.Log?.Error("[speech] PrismHandler: prism_init returned NULL.");
                    return false;
                }
                // Start on the best available backend; the config's chosen backend is applied on first speak.
                var slot = EnsureSlot(AutoBackend);
                if (slot == null || slot.Handle == IntPtr.Zero)
                {
                    Main.Log?.Error("[speech] PrismHandler: no backend could be acquired.");
                    Unload();
                    return false;
                }
                _current = slot;
                _backend = slot.Handle;
                _backendFeatures = slot.Features;
                _currentBackend = AutoBackend;
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler failed to load: " + ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            foreach (var slot in _slots.Values)
                if (slot != null && slot.Handle != IntPtr.Zero)
                {
                    try { PrismNative.BackendStop(slot.Handle); } catch { }
                    try { PrismNative.BackendFree(slot.Handle); } catch { }
                    slot.Handle = IntPtr.Zero;
                }
            _slots.Clear();
            _current = null;
            _backend = IntPtr.Zero;
            _backendFeatures = 0;
            _currentBackend = AutoBackend;
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.Shutdown(_ctx); } catch { }
                _ctx = IntPtr.Zero;
            }
        }

        // Apply a config's backend choice — a SLOT LOOKUP, never a teardown (see BackendSlot).
        // A choice that can't be acquired keeps whatever's already working (the failure is cached in
        // its slot, so it isn't re-attempted per utterance). Never strand a blind user with no voice.
        private void ApplyConfig(CategorySetting config)
        {
            var pref = config?.Get<ChoiceSetting>("backend")?.Current?.Id ?? AutoBackend;
            if (pref != _currentBackend || _backend == IntPtr.Zero)
            {
                var slot = EnsureSlot(pref);
                if (slot == null || slot.Handle == IntPtr.Zero)
                {
                    if (_currentBackend != pref)
                        Main.Log?.Error("[speech] PrismHandler: backend '" + pref + "' could not be acquired; keeping current backend.");
                    _currentBackend = pref;
                    return;
                }
                _current = slot;
                _backend = slot.Handle;
                _backendFeatures = slot.Features;
                _currentBackend = pref;
            }
            ApplyParams(config);
        }

        // Apply the config's rate/volume/voice to the bound backend, each gated on its feature bits —
        // a screen reader that owns its own rate simply doesn't advertise the knob and the request is
        // skipped. Voice matches by NAME at apply time (Prism voice ids are session-local indices).
        private void ApplyParams(CategorySetting config)
        {
            if (_backend == IntPtr.Zero || config == null || _current == null) return;
            int rate = config.Get<IntSetting>("rate")?.Get() ?? 50;
            int volume = config.Get<IntSetting>("volume")?.Get() ?? 100;
            string voice = config.Get<ChoiceSetting>("voice")?.ValueId ?? "default";

            if (rate != _current.AppliedRate)
            {
                if ((_backendFeatures & PrismNative.BackendFeatures.SupportsSetRate) != 0)
                    try { PrismNative.BackendSetRate(_backend, rate / 100f); } catch { }
                _current.AppliedRate = rate;
            }
            if (volume != _current.AppliedVolume)
            {
                if ((_backendFeatures & PrismNative.BackendFeatures.SupportsSetVolume) != 0)
                    try { PrismNative.BackendSetVolume(_backend, volume / 100f); } catch { }
                _current.AppliedVolume = volume;
            }
            if (voice != _current.AppliedVoice)
            {
                // "default" = leave the backend's own voice untouched (never force a pick).
                if (voice != "default"
                    && (_backendFeatures & PrismNative.BackendFeatures.SupportsSetVoice) != 0
                    && (_backendFeatures & PrismNative.BackendFeatures.SupportsCountVoices) != 0)
                    try { SetVoiceByName(voice); } catch { }
                _current.AppliedVoice = voice;
            }
        }

        private void SetVoiceByName(string name)
        {
            if (PrismNative.BackendCountVoices(_backend, out var vc) != PrismNative.PrismError.Ok) return;
            int count = (int)vc.ToUInt64();
            for (int v = 0; v < count; v++)
                if (PrismNative.BackendGetVoiceName(_backend, (ulong)v) == name)
                {
                    var err = PrismNative.BackendSetVoice(_backend, (UIntPtr)(ulong)v);
                    if (err != PrismNative.PrismError.Ok)
                        Main.Log?.Log("[speech] Prism set_voice '" + name + "' -> " + err);
                    return;
                }
            // A voice picked for OneCore doesn't exist on e.g. NVDA — normal, keep the backend's own.
            Main.Log?.Log("[speech] Prism voice '" + name + "' not found on backend '"
                + (PrismNative.BackendName(_backend) ?? "?") + "'; keeping its current voice.");
        }

        public bool Speak(string text, bool interrupt, CategorySetting config)
        {
            ApplyConfig(config);
            if (_backend == IntPtr.Zero) return false;
            try
            {
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt, CategorySetting config)
        {
            ApplyConfig(config);
            if (_backend == IntPtr.Zero) return false;
            try
            {
                // prism_backend_output drives both speech and braille when supported; otherwise fall
                // through to plain speak so we still produce audio.
                if ((_backendFeatures & PrismNative.BackendFeatures.SupportsOutput) != 0)
                {
                    var err = PrismNative.BackendOutput(_backend, text, interrupt);
                    if (err == PrismNative.PrismError.Ok) return true;
                    if (err != PrismNative.PrismError.NotImplemented)
                        Main.Log?.Log("[speech] PrismHandler.Output -> " + err + ", falling back to Speak.");
                }
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Output failed: " + ex.Message);
                return false;
            }
        }

        public bool Silence()
        {
            if (_backend == IntPtr.Zero) return false;
            try
            {
                return PrismNative.BackendStop(_backend) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Silence failed: " + ex.Message);
                return false;
            }
        }

        // Render-to-PCM (positional speech) rides prism_backend_speak_to_memory — OneCore implements
        // it (synchronous, silence-trimmed float samples); screen readers don't. Answered for THE
        // CONFIG'S OWN backend slot — the old current-backend property raced with whichever config
        // spoke last (default SR spoke → events' OneCore reported "can't render" → live fallback →
        // no panning; tester repro).
        public bool SupportsAudioRender(CategorySetting config)
        {
            var pref = config?.Get<ChoiceSetting>("backend")?.Current?.Id ?? AutoBackend;
            var slot = EnsureSlot(pref);
            return slot != null && slot.Handle != IntPtr.Zero
                && (slot.Features & PrismNative.BackendFeatures.SupportsSpeakToMemory) != 0;
        }

        public SpeechAudio RenderToAudio(string text, CategorySetting config)
        {
            ApplyConfig(config);
            if (_backend == IntPtr.Zero
                || (_backendFeatures & PrismNative.BackendFeatures.SupportsSpeakToMemory) == 0) return null;

            // Accumulate defensively (the contract allows per-chunk delivery; OneCore sends one) and
            // convert float [-1,1] to the 16-bit little-endian PCM SpeechAudio carries.
            var chunks = new List<byte[]>();
            int sampleRate = 0, channels = 0, totalBytes = 0;
            PrismNative.AudioCallback cb = (userdata, samples, sampleCount, ch, sr) =>
            {
                int n = (int)sampleCount.ToUInt64();
                if (n <= 0 || samples == IntPtr.Zero) return;
                var floats = new float[n];
                System.Runtime.InteropServices.Marshal.Copy(samples, floats, 0, n);
                var pcm = new byte[n * 2];
                for (int i = 0; i < n; i++)
                {
                    float f = floats[i];
                    if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                    short s = (short)(f * 32767f);
                    pcm[2 * i] = (byte)(s & 0xFF);
                    pcm[2 * i + 1] = (byte)((s >> 8) & 0xFF);
                }
                chunks.Add(pcm);
                totalBytes += pcm.Length;
                sampleRate = (int)sr.ToUInt64();
                channels = (int)ch.ToUInt64();
            };
            try
            {
                var err = PrismNative.BackendSpeakToMemory(_backend, text, cb);
                if (err != PrismNative.PrismError.Ok)
                {
                    Main.Log?.Log("[speech] Prism speak_to_memory -> " + err);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] Prism RenderToAudio failed: " + ex.Message);
                return null;
            }
            finally { GC.KeepAlive(cb); }
            if (totalBytes == 0 || sampleRate <= 0 || channels <= 0) return null;

            byte[] all;
            if (chunks.Count == 1) all = chunks[0];
            else
            {
                all = new byte[totalBytes];
                int off = 0;
                foreach (var c in chunks) { Buffer.BlockCopy(c, 0, all, off, c.Length); off += c.Length; }
            }
            return new SpeechAudio { Pcm = all, SampleRate = sampleRate, Channels = channels, BitsPerSample = 16 };
        }

        /// <summary>Build a ready-to-use backend for the preference — the named backend if it can be acquired,
        /// otherwise the best available (auto). Returns zero ONLY when nothing at all can be acquired. Does not
        /// touch the active <see cref="_backend"/>, so callers can acquire-then-swap safely.</summary>
        private IntPtr ResolveBackend(string preferred)
        {
            if (_ctx == IntPtr.Zero) return IntPtr.Zero;
            preferred = preferred ?? AutoBackend;

            IntPtr backend = IntPtr.Zero;
            if (preferred != AutoBackend)
            {
                backend = AcquireNamed(preferred);
                if (backend == IntPtr.Zero)
                    Main.Log?.Log("[speech] Prism backend '" + preferred + "' unavailable; falling back to auto (best available).");
            }
            if (backend == IntPtr.Zero) backend = PrismNative.RegistryCreateBest(_ctx); // first working
            return backend;
        }

        // Create + initialize the registry backend whose name matches. Zero on ANY failure (not in registry,
        // create returned null, or init failed) — the caller then falls back to the best available.
        private IntPtr AcquireNamed(string preferred)
        {
            var count = (int)PrismNative.RegistryCount(_ctx).ToUInt64();
            ulong id = 0;
            for (int i = 0; i < count; i++)
            {
                var candidate = PrismNative.RegistryIdAt(_ctx, (UIntPtr)(uint)i);
                if (PrismNative.RegistryName(_ctx, candidate) == preferred) { id = candidate; break; }
            }
            if (id == 0) { Main.Log?.Log("[speech] Prism backend '" + preferred + "' not in registry."); return IntPtr.Zero; }

            var backend = PrismNative.RegistryCreate(_ctx, id);
            if (backend == IntPtr.Zero) { Main.Log?.Log("[speech] Prism backend '" + preferred + "' create returned null."); return IntPtr.Zero; }

            var initErr = PrismNative.BackendInitialize(backend);
            if (initErr != PrismNative.PrismError.Ok && initErr != PrismNative.PrismError.AlreadyInitialized)
            {
                Main.Log?.Log("[speech] Prism backend '" + preferred + "' init failed (" + initErr + ").");
                PrismNative.BackendFree(backend);
                return IntPtr.Zero;
            }
            return backend;
        }

        // Adopt a freshly-acquired backend as the active one and cache its features (the query does real work
        // per call on some backends, and features don't change after init).
    }
}
