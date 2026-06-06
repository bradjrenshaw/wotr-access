using System;
using System.Text;
using UnityEngine;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A mod-owned single-line text editor (mod-pushed, raw-input) for arbitrary mod strings — e.g. an
    /// overlay's name. Unlike the game-field text entry it needs no TMP_InputField: it sets
    /// <see cref="Screen.CapturesRawInput"/> so our nav stands down and reads
    /// <see cref="UnityEngine.Input.inputString"/> each frame (printable chars, backspace, Enter), echoing
    /// each keystroke for the screen reader. Enter confirms (fires the callback), Escape cancels. Focus mode
    /// stays on (the menu engaged it), so the game's letter hotkeys stay muted while typing.
    /// </summary>
    public sealed class ModTextEntryScreen : Screen
    {
        private static string s_label;
        private static StringBuilder s_buf;
        private static Action<string> s_onConfirm;
        private static bool s_active;
        private static bool s_armed; // the opening key (the Enter that got us here) has released

        public static void Open(string label, string initial, Action<string> onConfirm)
        {
            s_label = label;
            s_buf = new StringBuilder(initial ?? "");
            s_onConfirm = onConfirm;
            s_active = true;
            s_armed = false;
        }

        public override string Key => "overlay.modtextentry";
        public override int Layer => 38; // above mod menu (35) / key capture (36) / choice submenu (37), below tooltip (40)
        public override bool CapturesRawInput => true;
        public override bool IsActive() => s_active;

        public override void OnFocus()
        {
            var cur = s_buf != null && s_buf.Length > 0 ? s_buf.ToString() : Loc("text.blank", "blank");
            Tts.Speak((s_label ?? Loc("text.prompt", "Edit text")) + ". " + cur + ". "
                + Loc("text.help", "Type to edit, Backspace to delete, Enter to confirm, Escape to cancel."));
        }

        public override void OnUpdate()
        {
            if (!s_active) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Tts.Speak(Loc("text.cancelled", "Cancelled"));
                Close();
                return;
            }

            // Wait for the opening key to release, else its '\n' confirms immediately.
            if (!s_armed)
            {
                if (!UnityEngine.Input.anyKey) s_armed = true;
                return;
            }

            var typed = UnityEngine.Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            foreach (char c in typed)
            {
                if (c == '\b')
                {
                    if (s_buf.Length > 0)
                    {
                        char removed = s_buf[s_buf.Length - 1];
                        s_buf.Remove(s_buf.Length - 1, 1);
                        Echo(removed);
                    }
                    else Tts.Speak(Loc("text.blank", "blank"), interrupt: true);
                }
                else if (c == '\n' || c == '\r')
                {
                    var value = s_buf.ToString().Trim();
                    var cb = s_onConfirm;
                    Close();
                    cb?.Invoke(value);
                    return;
                }
                else if (!char.IsControl(c))
                {
                    s_buf.Append(c);
                    Echo(c);
                }
            }
        }

        // Echo a typed/deleted character (space spoken as the word, since TTS skips a bare space).
        private static void Echo(char c)
            => Tts.Speak(c == ' ' ? Loc("text.space", "space") : c.ToString(), interrupt: true);

        private static void Close()
        {
            s_active = false;
            s_onConfirm = null;
            s_buf = null;
        }

        private static string Loc(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
