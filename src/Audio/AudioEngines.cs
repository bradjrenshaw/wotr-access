using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Entry point to the audio backend. Two engines behind one <see cref="IAudioEngine"/> surface,
    /// picked by the <c>audio.mixer</c> setting: <c>loadstar</c> (the DEFAULT: the user's native
    /// Rust mixer — miniaudio device layer, ~2.7 ms periods on its own MMCSS thread, GC-immune)
    /// and <c>naudio</c> (the managed mixer on a WaveOut buffer — works everywhere, 50 ms cushion
    /// against Mono GC pauses). Loadstar falls back to NAudio when its dll or a device is missing. Switching
    /// live disposes the old engine and bumps <see cref="Generation"/>, which owners of long-lived
    /// voices (the wall-tone system) watch to recreate theirs on the new engine.
    /// (Named <c>AudioEngines</c> rather than <c>Audio</c> to avoid colliding with the namespace.)
    /// </summary>
    internal static class AudioEngines
    {
        private static IAudioEngine _current;

        /// <summary>Bumped on every backend switch / shutdown: voices created on an older generation
        /// belong to a disposed engine and must be recreated.</summary>
        public static int Generation { get; private set; }

        public static string BackendId =>
            ModSettings.GetSetting<ChoiceSetting>("audio.mixer")?.Current?.Id ?? "loadstar";

        public static IAudioEngine Current
        {
            get { if (_current == null) _current = Create(); return _current; }
        }

        /// <summary>The NAudio engine when it is the active backend, else null (its WaveOut-specific
        /// knobs — the latency slider — only apply there).</summary>
        public static NAudioEngine NAudio => _current as NAudioEngine;

        private static IAudioEngine Create()
        {
            if (BackendId == "loadstar")
            {
                var e = LoadstarEngine.TryOpen();
                if (e != null) return e;
                Main.Log?.Warning("[audio] Loadstar backend unavailable — using NAudio");
            }
            return new NAudioEngine();
        }

        /// <summary>The backend setting changed: drop the current engine; the next use opens the new
        /// one. Existing wall-tone voices die with the old engine (their owner recreates them).</summary>
        public static void Reselect()
        {
            var old = _current;
            _current = null;
            Generation++;
            try { old?.Dispose(); } catch { }
        }

        /// <summary>Per-frame: device watchdogs / finished polls. Only ticks an engine that already
        /// exists — never starts one.</summary>
        public static void Tick() => _current?.Tick();

        /// <summary>Close the output device and drop the engine (module hot-reload teardown) — a
        /// leaked output would keep mixing the OLD generation's voices over the new one's.</summary>
        public static void ShutdownAll()
        {
            var old = _current;
            _current = null;
            Generation++;
            try { old?.Dispose(); } catch { }
        }
    }
}
