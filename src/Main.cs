using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using UnityEngine;
using UnityModManagerNet;
using WrathAccess.Exploration.Overlays;
using WrathAccess.Input;
using WrathAccess.Screens;
using WrathAccess.UI; // NavDirection

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
                GameLogReader.Initialize(); // read barks + narrative log lines (no dialogue window)

                Log.Log("WrathAccess initialized. " + BuildStamp());
                Tts.Speak("Wrath Access loaded.");
            }
            catch (Exception e)
            {
                Log.Error("Initialization failed: " + e);
                return false;
            }

            return true;
        }

        // The loaded DLL's build time + path, logged at startup so we can confirm from Player.log which
        // build is actually running (UMM loads the assembly at launch — code changes need a game restart).
        private static string BuildStamp()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return "Build " + System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") + " (" + path + ")";
            }
            catch { return "Build ?"; }
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

        // Space's exploration job: toggle the game's pause. Only when focus mode owns the keyboard AND
        // we're in plain exploration — focus off lets the game's own Space pause, and on a UI screen the
        // navigator consumed Space for the tooltip (so this never fires there). Uses the game's own
        // PauseBind so combat/global-map are handled exactly as the native binding does.
        private static void TogglePauseIfExploring()
        {
            if (!FocusMode.Active) return;
            var current = ScreenManager.Current;
            if (current == null || current.Key != "ctx.ingame") return;
            var game = Game.Instance;
            if (game == null) return;
            bool wasPaused = game.IsPaused;
            game.PauseBind();
            Tts.Speak(wasPaused ? "Unpaused" : "Paused");
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
            // The arrows' Performed handler fires only when the navigator DIDN'T consume them — i.e.
            // in-game with no UI focus tree — where it drives the active area overlay's cursor instead.
            InputManager.Register("nav.up", "Navigate Up", () => OverlayManager.Move(NavDirection.Up)).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("nav.down", "Navigate Down", () => OverlayManager.Move(NavDirection.Down)).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("nav.left", "Navigate Left", () => OverlayManager.Move(NavDirection.Left)).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("nav.right", "Navigate Right", () => OverlayManager.Move(NavDirection.Right)).AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("nav.primary", "Primary action").AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("nav.secondary", "Secondary action").AddBinding(KeyCode.Backspace);
            InputManager.Register("nav.back", "Back").AddBinding(KeyCode.Escape);
            // Space: in a UI state it reads the focused control's tooltip (handled by the navigator); in
            // plain exploration the navigator stands down and this fires instead — the game's pause toggle,
            // matching the game's own Space binding. (A move queued while paused only walks once unpaused.)
            InputManager.Register("focus.tooltip", "Read tooltip / toggle pause",
                TogglePauseIfExploring).AddBinding(KeyCode.Space);
            InputManager.Register("nav.next", "Next (Tab)").AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("nav.prev", "Previous (Shift+Tab)").AddBinding(KeyCode.Tab, shift: true).Repeating();

            // Exploration scanner: a categorized, distance-sorted list of things in the current area.
            // Active only in the in-game context while focus mode owns the keyboard (Scanner gates itself).
            InputManager.Register("scan.itemNext", "Scanner: next item",
                WrathAccess.Exploration.Scanner.NextItem).AddBinding(KeyCode.PageDown).Repeating();
            InputManager.Register("scan.itemPrev", "Scanner: previous item",
                WrathAccess.Exploration.Scanner.PrevItem).AddBinding(KeyCode.PageUp).Repeating();
            InputManager.Register("scan.categoryNext", "Scanner: next category",
                WrathAccess.Exploration.Scanner.NextCategory).AddBinding(KeyCode.PageDown, ctrl: true).Repeating();
            InputManager.Register("scan.categoryPrev", "Scanner: previous category",
                WrathAccess.Exploration.Scanner.PrevCategory).AddBinding(KeyCode.PageUp, ctrl: true).Repeating();
            InputManager.Register("scan.cursorToItem", "Scanner: move cursor to item",
                WrathAccess.Exploration.Scanner.CursorToSelected).AddBinding(KeyCode.Home);
            InputManager.Register("scan.announceCursor", "Announce cursor position",
                WrathAccess.Exploration.Scanner.AnnounceCursor).AddBinding(KeyCode.K);
            InputManager.Register("scan.announceParty", "Announce party",
                WrathAccess.Exploration.Scanner.AnnounceParty).AddBinding(KeyCode.K, shift: true);
            InputManager.Register("scan.interact", "Scanner: interact with item",
                WrathAccess.Exploration.Scanner.InteractSelected).AddBinding(KeyCode.I);
            InputManager.Register("scan.moveToCursor", "Scanner: move to cursor",
                WrathAccess.Exploration.Scanner.MoveToCursor).AddBinding(KeyCode.Backspace);

            // Area overlays: swappable spatial views (first: virtual tile view). Arrows drive the active
            // overlay's cursor (see nav.* above). These verbs gate themselves to focus-mode exploration.
            InputManager.Register("overlay.cycle", "Cycle area overlay",
                OverlayManager.Cycle).AddBinding(KeyCode.O, ctrl: true);
            InputManager.Register("overlay.recenter", "Overlay: recenter on player",
                OverlayManager.Recenter).AddBinding(KeyCode.C);
            InputManager.Register("overlay.announce", "Overlay: announce cursor",
                OverlayManager.AnnounceCurrent).AddBinding(KeyCode.Keypad5);
            InputManager.Register("overlay.descend", "Overlay: follow surface down",
                () => OverlayManager.VerticalFollow(-1)).AddBinding(KeyCode.Period);
            InputManager.Register("overlay.ascend", "Overlay: follow surface up",
                () => OverlayManager.VerticalFollow(1)).AddBinding(KeyCode.Comma);
        }
    }
}
