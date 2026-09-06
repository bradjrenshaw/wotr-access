namespace WrathAccess.Audio
{
    /// <summary>
    /// Entry point to the audio backend — our NAudio mixer, the ONE engine (the Wwise path was retired:
    /// buggy in several ways, and the NAudio spatializer sounds better; the game's own Wwise engine is
    /// still configured via WwiseRuntimeConfig, but the mod plays nothing through it).
    /// (Named <c>AudioEngines</c> rather than <c>Audio</c> to avoid colliding with the namespace.)
    /// </summary>
    internal static class AudioEngines
    {
        private static NAudioEngine _naudio;

        public static NAudioEngine NAudio
        {
            get { if (_naudio == null) _naudio = new NAudioEngine(); return _naudio; }
        }

        /// <summary>Per-frame: keeps the output bound to a live device (headset unplug / re-plug).
        /// Only ticks an engine that already exists — never starts one.</summary>
        public static void Tick() => _naudio?.Tick();

        /// <summary>Close the output device and drop the engine (module hot-reload teardown) — a
        /// leaked WaveOutEvent would keep mixing the OLD generation's voices over the new one's.</summary>
        public static void ShutdownAll()
        {
            _naudio?.Dispose();
            _naudio = null;
        }
    }
}
