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
        private static List<Choice> _backendChoices;   // enumerated once (the registry probe is expensive)

        public string Key => "prism";
        public string Label => "Prism";
        public string LocalizationKey => "speech.prism";

        public void BuildSettings(CategorySetting into)
        {
            into.Add(new ChoiceSetting("backend", "Backend", BackendChoices(), AutoBackend, "speech.prism.backend"));
        }

        // Enumerate prism's registry once and keep only backends whose engine is actually available on
        // this machine (SupportedAtRuntime filters out the obviously-irrelevant, e.g. JAWS with no JAWS).
        private static List<Choice> BackendChoices()
        {
            if (_backendChoices != null) return _backendChoices;
            var choices = new List<Choice> { new Choice(AutoBackend, "Auto (Best Available)", "speech.backend_auto") };
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
                                    choices.Add(new Choice(name, name)); // backend names are product names — not translated
                            }
                            finally { PrismNative.BackendFree(backend); }
                        }
                    }
                    finally { PrismNative.Shutdown(probeCtx); }
                }
            }
            catch (DllNotFoundException) { /* prism.dll missing — the bare Auto choice remains */ }
            catch (Exception ex) { Main.Log?.Warning("[speech] Prism backend enumeration failed: " + ex.Message); }
            _backendChoices = choices;
            return _backendChoices;
        }

        public bool Detect()
        {
            try
            {
                var ctx = PrismNative.Init(IntPtr.Zero);
                if (ctx == IntPtr.Zero) return false;
                try
                {
                    var backend = PrismNative.RegistryCreateBest(ctx);
                    if (backend == IntPtr.Zero) return false;
                    PrismNative.BackendFree(backend);
                    return true;
                }
                finally { PrismNative.Shutdown(ctx); }
            }
            catch (DllNotFoundException) { return false; }
            catch (Exception ex)
            {
                Main.Log?.Log("[speech] PrismHandler.Detect failed: " + ex.Message);
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
                _currentBackend = AutoBackend;
                return AcquireBackend(AutoBackend); // the config's backend is applied on first speak
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
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.BackendStop(_backend); } catch { }
                try { PrismNative.BackendFree(_backend); } catch { }
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.Shutdown(_ctx); } catch { }
                _ctx = IntPtr.Zero;
            }
            _backendFeatures = 0;
            _currentBackend = AutoBackend;
        }

        // Apply a config's backend choice, rebinding only when it differs from what's bound (rebinding is
        // a native teardown/acquire — never per utterance).
        private void ApplyConfig(CategorySetting config)
        {
            var pref = config?.Get<ChoiceSetting>("backend")?.Current?.Id ?? AutoBackend;
            if (pref == _currentBackend && _backend != IntPtr.Zero) return;
            _currentBackend = pref;
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.BackendStop(_backend); } catch { }
                PrismNative.BackendFree(_backend);
                _backend = IntPtr.Zero;
                _backendFeatures = 0;
            }
            AcquireBackend(pref);
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

        // Prism's API has a SupportsSpeakToMemory feature flag, so a render path is possible later;
        // the binding isn't ported yet.
        public bool SupportsAudioRender => false;
        public SpeechAudio RenderToAudio(string text, CategorySetting config) => null;

        /// <summary>Acquire the named backend (auto = highest-priority that initializes; otherwise the
        /// named registry backend, falling back to auto).</summary>
        private bool AcquireBackend(string preferred)
        {
            if (_ctx == IntPtr.Zero) return false;
            preferred = preferred ?? AutoBackend;

            if (preferred == AutoBackend)
            {
                _backend = PrismNative.RegistryCreateBest(_ctx);
            }
            else
            {
                var count = (int)PrismNative.RegistryCount(_ctx).ToUInt64();
                ulong id = 0;
                for (int i = 0; i < count; i++)
                {
                    var candidate = PrismNative.RegistryIdAt(_ctx, (UIntPtr)(uint)i);
                    if (PrismNative.RegistryName(_ctx, candidate) == preferred) { id = candidate; break; }
                }
                if (id == 0)
                {
                    Main.Log?.Error("[speech] PrismHandler: backend '" + preferred + "' not in registry; using auto.");
                    _backend = PrismNative.RegistryCreateBest(_ctx);
                }
                else
                {
                    _backend = PrismNative.RegistryCreate(_ctx, id);
                    if (_backend != IntPtr.Zero)
                    {
                        var initErr = PrismNative.BackendInitialize(_backend);
                        if (initErr != PrismNative.PrismError.Ok && initErr != PrismNative.PrismError.AlreadyInitialized)
                        {
                            Main.Log?.Error("[speech] PrismHandler: backend '" + preferred + "' init failed (" + initErr + "); using auto.");
                            PrismNative.BackendFree(_backend);
                            _backend = PrismNative.RegistryCreateBest(_ctx);
                        }
                    }
                }
            }

            if (_backend == IntPtr.Zero)
            {
                Main.Log?.Error("[speech] PrismHandler: no backend could be acquired.");
                return false;
            }

            // Cache the feature bitmask: on some backends (notably NVDA) the query does real work per
            // call. Features don't change after init.
            _backendFeatures = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(_backend);
            Main.Log?.Log("[speech] PrismHandler loaded. Backend: " + (PrismNative.BackendName(_backend) ?? "<unknown>")
                + " (features=0x" + ((ulong)_backendFeatures).ToString("X") + ")");
            return true;
        }
    }
}
