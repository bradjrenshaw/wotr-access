using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>Last-resort fallback: every announcement is copied to the clipboard (readable with any
    /// clipboard tool). Ported from SayTheSpire2; Unity's copy buffer instead of Godot's.</summary>
    public class ClipboardHandler : ISpeechHandler
    {
        public string Key => "clipboard";
        public string Label => "Clipboard";
        public string LocalizationKey => "speech.clipboard";
        public CategorySetting GetSettings() => null;

        public bool Detect() => true;

        public bool Load()
        {
            Main.Log?.Log("[speech] Clipboard handler loaded (fallback).");
            return true;
        }

        public void Unload() { }

        public bool Speak(string text, bool interrupt = false) => Output(text, interrupt);

        public bool Output(string text, bool interrupt = false)
        {
            UnityEngine.GUIUtility.systemCopyBuffer = text;
            return true;
        }

        public bool Silence() => true;

        public bool SupportsAudioRender => false;
        public SpeechAudio RenderToAudio(string text) => null;
    }
}
