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
    }
}
