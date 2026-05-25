using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using WrathAccess.Input;
using WrathAccess.Screens;

namespace WrathAccess
{
    /// <summary>
    /// Unity Mod Manager entry point. UMM finds <c>WrathAccess.Main.Load</c>
    /// (declared in Info.json's EntryMethod) and calls it once at game start.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Log;

        /// <summary>Master switch, flipped from UMM's mod list (OnToggle).</summary>
        public static bool Enabled = true;

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = modEntry.Logger;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUpdate = OnUpdate;

            try
            {
                Tts.Initialize();
                _harmony = new Harmony(modEntry.Info.Id);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                RegisterInput();
                ScreenManager.Initialize();

                Log.Log("WrathAccess initialized.");
                Tts.Speak("Wrath Access loaded.");
            }
            catch (Exception e)
            {
                Log.Error("Initialization failed: " + e);
                return false;
            }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value) FocusMode.Set(false);
            Log.Log("WrathAccess " + (value ? "enabled" : "disabled"));
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!Enabled) return;
            InputManager.Tick();
            ScreenManager.Tick();
        }

        /// <summary>
        /// Temporary proof-of-life bindings for the input/speech/suppression slice.
        /// These get replaced by real navigation + a rebindable action set.
        /// </summary>
        private static void RegisterInput()
        {
            // Global hotkeys (fire when the navigator doesn't consume them).
            InputManager.Register("toggle_focus", "Toggle Focus Mode", () =>
            {
                FocusMode.Toggle();
                Tts.Speak("Focus mode " + (FocusMode.Active ? "on" : "off"));
                if (FocusMode.Active) WrathAccess.UI.Navigation.AnnounceCurrent();
            }).AddBinding(KeyCode.A, ctrl: true, shift: true);

            InputManager.Register("speak_test", "Speak Test", () =>
                Tts.Speak("Wrath Access input and speech are working."))
                .AddBinding(KeyCode.T, ctrl: true, shift: true);

            // Navigation actions (consumed by the active navigator while focus mode is on).
            // Movement actions auto-repeat while held (Repeating); activation keys do not.
            InputManager.Register("nav.up", "Navigate Up").AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("nav.down", "Navigate Down").AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("nav.left", "Navigate Left").AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("nav.right", "Navigate Right").AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("nav.primary", "Primary action").AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("nav.secondary", "Secondary action").AddBinding(KeyCode.Backspace);
            InputManager.Register("nav.back", "Back").AddBinding(KeyCode.Escape);
            InputManager.Register("focus.tooltip", "Read tooltip").AddBinding(KeyCode.Space);
            InputManager.Register("nav.next", "Next (Tab)").AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("nav.prev", "Previous (Shift+Tab)").AddBinding(KeyCode.Tab, shift: true).Repeating();
        }
    }
}
