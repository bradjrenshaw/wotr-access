using System.Collections.Generic;
using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Captures a key combo for one of OUR <see cref="InputAction"/>s (mod-pushed, distinct from the
    /// game-binding <see cref="KeyBindCaptureScreen"/>). It sets <see cref="Screen.CapturesRawInput"/> so
    /// the InputManager stands down (our nav won't eat the keypress), and reads the next non-modifier
    /// keydown directly in <see cref="OnUpdate"/>, capturing the current Ctrl/Shift/Alt state. Focus mode
    /// stays on (the mod menu engaged it), so the game's own keys remain muted. Escape cancels.
    /// </summary>
    public sealed class ModKeyCaptureScreen : Screen
    {
        private static InputAction s_action;
        private static bool s_armed;        // the key that opened the dialog has released; ready to capture
        private static bool s_awaitRelease; // bound; staying up until the confirming key releases
        private static KeyCode s_releaseKey;

        public static void Open(InputAction action) { s_action = action; s_armed = false; s_awaitRelease = false; }

        public override string Key => "overlay.modkeycapture";
        public override int Layer => 36; // just above the mod menu (35)
        public override bool CapturesRawInput => true;
        public override bool IsActive() => s_action != null;

        public override void OnFocus()
            => Tts.Speak("Rebind " + (s_action != null ? s_action.Label : "") + ". Press a key, or Escape to cancel.");

        public override void OnUpdate()
        {
            var action = s_action;
            if (action == null) return;

            // After a successful bind, keep the screen up (input still stood down) until the confirming key
            // is released — otherwise that same press propagates into the menu/new binding (a cascade).
            if (s_awaitRelease)
            {
                if (!UnityEngine.Input.GetKey(s_releaseKey)) { s_action = null; s_awaitRelease = false; }
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { Tts.Speak("Cancelled"); s_action = null; return; }

            // Wait for the opening key (the Enter/activate press that got us here) to release before we
            // start reading — otherwise that very press is captured as the new binding.
            if (!s_armed)
            {
                if (!UnityEngine.Input.anyKey) s_armed = true;
                return;
            }

            if (!UnityEngine.Input.anyKeyDown) return;

            foreach (var key in CaptureKeys)
            {
                if (!UnityEngine.Input.GetKeyDown(key)) continue;
                bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                var binding = new KeyboardBinding(key, ctrl, shift, alt);

                // Reject a combo already bound to another action — otherwise one keypress fires two actions.
                var conflict = FindConflict(binding, action);
                if (conflict != null)
                {
                    Tts.Speak(binding.DisplayName + " is already bound to " + conflict.Label
                        + ". Press another key, or Escape to cancel.", interrupt: true);
                    return; // keep capturing
                }

                action.ClearBindings();
                action.AddBinding(binding); // BindingsChanged → BindingSetting auto-saves
                Tts.Speak(action.Label + " bound to " + binding.DisplayName);
                s_releaseKey = key;
                s_awaitRelease = true; // close once the key is released
                return;
            }
        }

        // The action (other than the one being bound) that already uses this exact combo, or null.
        private static InputAction FindConflict(InputBinding binding, InputAction self)
        {
            foreach (var a in InputManager.Actions)
            {
                if (a == self) continue;
                foreach (var b in a.Bindings)
                    if (b.Type == binding.Type && b.Serialize() == binding.Serialize()) return a;
            }
            return null;
        }

        // Keyboard keys we can bind to: every KeyCode except None, the modifier keys, Escape, and the
        // mouse/joystick range (which start at Mouse0). Built once.
        private static KeyCode[] _captureKeys;
        private static KeyCode[] CaptureKeys
        {
            get
            {
                if (_captureKeys != null) return _captureKeys;
                var list = new List<KeyCode>();
                foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k == KeyCode.Escape) continue;
                    if (k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                        k == KeyCode.LeftShift || k == KeyCode.RightShift ||
                        k == KeyCode.LeftAlt || k == KeyCode.RightAlt) continue;
                    if ((int)k >= (int)KeyCode.Mouse0) continue; // mouse + joystick buttons
                    list.Add(k);
                }
                _captureKeys = list.ToArray();
                return _captureKeys;
            }
        }
    }
}
