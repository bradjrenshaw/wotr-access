using DavyKager;

namespace WrathAccess
{
    /// <summary>
    /// Thin wrapper over Tolk, which routes text to whatever screen reader is
    /// running (NVDA, JAWS) or to SAPI as a fallback. Native Tolk.dll and the
    /// screen-reader client dlls must sit next to Wrath.exe.
    /// </summary>
    public static class Tts
    {
        private static bool _loaded;

        public static void Initialize()
        {
            if (_loaded) return;
            Tolk.Load();
            _loaded = true;
            var reader = Tolk.DetectScreenReader();
            Main.Log?.Log("Tolk loaded. Screen reader: " + (reader ?? "none (SAPI fallback)"));
        }

        /// <summary>
        /// Speak <paramref name="text"/>. We never interrupt by default — queued
        /// speech is the user's preference (carried over from SayTheSpire).
        /// </summary>
        public static void Speak(string text, bool interrupt = false)
        {
            if (!_loaded || string.IsNullOrEmpty(text)) return;
            text = TextUtil.StripRichText(text); // game labels are TMP rich text
            if (string.IsNullOrEmpty(text)) return;
            Tolk.Output(text, interrupt);
        }

        public static void Stop()
        {
            if (_loaded) Tolk.Silence();
        }
    }
}
