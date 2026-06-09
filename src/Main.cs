using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.GameModes; // GameModeType
using UnityEngine;
using UnityModManagerNet;
using WrathAccess.Audio;
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
                WrathAccess.Localization.LocalizationManager.Initialize(); // wire Message's resolver early
                _harmony = new Harmony(modEntry.Info.Id);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                // Verify the pointer-cursor patch actually attached (this class of bug has bitten us before).
                var tick = HarmonyLib.AccessTools.Method(typeof(Kingmaker.Controllers.Clicks.PointerController), "Tick");
                var pinfo = tick != null ? Harmony.GetPatchInfo(tick) : null;
                Log.Log("[patch] PointerController.Tick found=" + (tick != null)
                    + " postfixes=" + (pinfo?.Postfixes?.Count ?? 0));
                RegisterInput();
                // Mod settings tree (the mod menu's tabs are its top-level categories). Built before load
                // so saved config (settings.json under the persistent data dir) overrides the defaults.
                BuildSettings();
                WrathAccess.Settings.ModSettings.Initialize(
                    System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "WrathAccess"));
                // Overlays are built AFTER load: the saved overlay-id list (incl. user-added ones) is only
                // known once settings have loaded, then their saved values are re-applied to the new subtrees.
                WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.BuildOverlays();
                ScreenManager.Initialize();
                GameLogReader.Initialize(); // read barks + narrative log lines (no dialogue window)
                WarningReader.Initialize(); // speak the game's "can't do that" warnings (refusal reasons)

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
            WrathAccess.Localization.LocalizationManager.Tick(); // pick up a live game-language swap
            InputManager.Tick();
            ScreenManager.Tick();
            TickPause(); // announce the game's pause state whenever it changes (ours OR the game's own)
            TickControl(); // chime when a cutscene/scripted event takes or returns control of the party
            WrathAccess.Exploration.CombatMode.TickTurn(); // announce whose turn it is in turn-based combat
            WrathAccess.Exploration.WorldModel.Tick(); // refresh the area entity registry before consumers read it
            // Unscaled delta: the cursor is a real-time UI element — it must keep moving while the game is
            // paused (the game-scaled dt is 0 when paused, which froze continuous-mode movement).
            // Ticks the active overlay: movement modes (glide) update the cursor, then systems (sonar,
            // wall tones, fog/object cues) read the fresh position.
            OverlayManager.Tick(UnityEngine.Time.unscaledDeltaTime);
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
            if (WrathAccess.UI.Navigation.HasFocus) return; // focused in the HUD → Space is a UI key, not pause
            // Just toggle; TickPause announces the resulting state change (PauseBind is async, and the game
            // also pauses on its own — e.g. combat start — so we react to the state, not this keypress).
            Game.Instance?.PauseBind();
        }

        // Announce pause/unpause whenever the game's real pause state flips during gameplay — catching the
        // game's own auto-pauses (e.g. combat start), not just our Space toggle. Gated on the active screen
        // being a play context, so menus/dialogue that auto-pause don't redundantly say "Paused" (they
        // announce themselves). Nullable baseline: don't fire on the first sample, and reset out of play so
        // re-entry is silent.
        private static bool? _wasPaused;
        private static void TickPause()
        {
            var game = Game.Instance;
            var key = ScreenManager.Current?.Key;
            bool inPlay = game != null
                && (key == "ctx.ingame" || key == "ctx.tacticalcombat" || key == "ctx.globalmap");
            if (!inPlay) { _wasPaused = null; return; }
            bool paused = game.IsPaused;
            if (_wasPaused.HasValue && paused != _wasPaused.Value)
                Tts.Speak(paused ? "Paused" : "Unpaused");
            _wasPaused = paused;
        }

        // Chime when control of the party is lost to / regained from a cutscene or scripted event. The
        // authoritative signal is Game.Instance.CutsceneLock.Active (set by the cutscene "Lock Controls"
        // command) — the same flag the game uses to hide the HUD and stop camera-follow. Gated to being in a
        // local area (menus leave the lock false, so opening windows never chimes); nullable baseline so we
        // don't chime on the first sample or across area loads (entering mid-intro-cutscene chimes "gained"
        // only when it ends).
        private static readonly SfxPlayer _controlSfx = new SfxPlayer();
        private static bool? _hadControl;
        private static void TickControl()
        {
            var game = Game.Instance;
            if (game == null || game.RootUiContext == null || !game.RootUiContext.IsInGame) { _hadControl = null; return; }
            bool hasControl = !game.CutsceneLock.Active;
            if (_hadControl.HasValue && hasControl != _hadControl.Value)
                _controlSfx.Play(System.IO.Path.Combine(OverlayAudio.Dir, hasControl ? "control_gained.wav" : "control_lost.wav"), OverlayAudio.Master);
            _hadControl = hasControl;
        }

        // Game modes where a quick-save is sensible — normal play, paused, world/global map, kingdom, and
        // service/fullscreen windows. Not dialogue, cutscenes, rest, turn-based combat, loading, etc. This
        // approximates the game's own save-allowed set without reproducing its keybinding registration.
        private static readonly HashSet<GameModeType> QuickSaveModes = new HashSet<GameModeType>
        {
            GameModeType.Default, GameModeType.Pause, GameModeType.GlobalMap,
            GameModeType.Kingdom, GameModeType.KingdomSettlement, GameModeType.FullScreenUi,
        };

        // F5 quick-save. Only while focus mode owns the keyboard: F5 is the game's own quick-save key, but
        // focus mode suppresses the game's keyboard, so we stand in for it here; with focus off the game's
        // native F5 saves and ours must not fire (no double-save). Calls the game's own MakeQuickSave.
        private static void QuickSave()
        {
            if (!FocusMode.Active) return;
            var game = Game.Instance;
            if (game == null) return;
            if (!QuickSaveModes.Contains(game.CurrentMode)) { Tts.Speak("Can't quick save now"); return; }
            try { game.MakeQuickSave(); Tts.Speak("Quick saving"); }
            catch (Exception e) { Log.Error("[quicksave] " + e); Tts.Speak("Quick save failed"); }
        }

        // Flip the game's real-time-with-pause <-> turn-based combat mode. We only flip the EnableTurnBasedMode
        // setting — the game's GameSettingsController hooks its OnValueChanged and applies every state change
        // (combat mode switch, etc.), so we mirror the game's own toggle exactly rather than driving state.
        private static void ToggleCombatMode()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null || !rc.IsInGame) return; // only in a loaded area (where combat happens)
            var setting = Kingmaker.Settings.SettingsRoot.Game.TurnBased.EnableTurnBasedMode;
            bool nowTurnBased = !setting; // the mode we're switching TO
            // The game's own mode-change cue for the target mode, then flip (it confirms + propagates).
            UiSound.Play(nowTurnBased ? Kingmaker.UI.UISoundType.ChangeModeTBM : Kingmaker.UI.UISoundType.ChangeModeRTWP);
            setting.SetValueAndConfirm(nowTurnBased);
            Tts.Speak(nowTurnBased ? "Turn-based mode" : "Real-time mode");
        }

        /// <summary>
        /// Temporary proof-of-life bindings for the input/speech/suppression slice.
        /// These get replaced by real navigation + a rebindable action set.
        /// </summary>
        // The mod settings tree (its top-level categories become the mod menu's tabs): Input — every
        // input action as a rebindable BindingSetting — and UI — announcement-verbosity toggles the
        // AnnouncementComposer consults live (off → that part is no longer spoken anywhere).
        private static void BuildSettings()
        {
            var bindings = new WrathAccess.Settings.CategorySetting("bindings", "Input", localizationKey: "category.input");
            foreach (var a in InputManager.Actions)
                bindings.Add(new WrathAccess.Settings.BindingSetting(a));
            WrathAccess.Settings.ModSettings.Root.Add(bindings);

            // UI = per-announcement settings (global toggles) + per-element-type overrides, discovered
            // by reflection. Creates "announcements" + "ui" categories under the settings Root.
            WrathAccess.UI.Announcements.AnnouncementRegistry.RegisterDefaults();

            // Overlays = the data-driven area-overlay configs (one settings subtree per overlay); also
            // builds the live Overlay objects and installs them in OverlayManager.
            WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Register();

            // Audio = settings-wide master volume (every overlay sound system scales by it).
            var audio = new WrathAccess.Settings.CategorySetting("audio", "Audio", localizationKey: "category.audio");
            audio.Add(new WrathAccess.Settings.IntSetting("master_volume", "Master volume", 100, 0, 100, 5, "audio.master_volume"));
            WrathAccess.Settings.ModSettings.Root.Add(audio);
        }

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

            // Quick save (the game's own MakeQuickSave). Self-gates to focus mode + a save-allowed mode.
            InputManager.Register("game.quickSave", "Quick save", QuickSave).AddBinding(KeyCode.F5);

            // Party selection — drives the game's real selection, which decides move-to-cursor's
            // single-vs-formation behaviour. Ctrl+A = whole party; Ctrl+1..6 = a single member.
            InputManager.Register("party.selectAll", "Select whole party",
                WrathAccess.Exploration.PartySelection.SelectWholeParty).AddBinding(KeyCode.A, ctrl: true);
            for (int i = 0; i < 6; i++)
            {
                int idx = i; // capture per-iteration for the closure
                InputManager.Register("party.select" + (i + 1), "Select party member " + (i + 1),
                    () => WrathAccess.Exploration.PartySelection.SelectMember(idx))
                    .AddBinding(KeyCode.Alpha1 + i, ctrl: true);
            }

            // Navigation actions (consumed by the active navigator while focus mode is on).
            // Movement actions auto-repeat while held (Repeating); activation keys do not.
            // The arrows' Performed handler fires only when the navigator DIDN'T consume them — i.e.
            // in-game with no UI focus tree — where it drives the active area overlay's cursor instead.
            InputManager.Register("nav.up", "Navigate Up", () => OverlayManager.Move(MovementSlot.Primary, NavDirection.Up)).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("nav.down", "Navigate Down", () => OverlayManager.Move(MovementSlot.Primary, NavDirection.Down)).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("nav.left", "Navigate Left", () => OverlayManager.Move(MovementSlot.Primary, NavDirection.Left)).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("nav.right", "Navigate Right", () => OverlayManager.Move(MovementSlot.Primary, NavDirection.Right)).AddBinding(KeyCode.RightArrow).Repeating();
            // Enter activates the focused UI control; in plain exploration (no focus tree) the navigator
            // declines and this fires instead — our "left click": interact with the thing under the cursor.
            InputManager.Register("nav.primary", "Primary action / interact",
                WrathAccess.Exploration.Scanner.InteractAtCursor).AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("nav.secondary", "Secondary action").AddBinding(KeyCode.Backspace);
            // Escape: the navigator consumes it as Back inside a UI screen; in plain exploration it bubbles
            // here, where it cancels ability targeting if we're aiming (otherwise a no-op).
            InputManager.Register("nav.back", "Back",
                () => { if (WrathAccess.Exploration.Targeting.Aiming) WrathAccess.Exploration.Targeting.Cancel(); })
                .AddBinding(KeyCode.Escape);
            // Space: in a UI state it reads the focused control's tooltip (handled by the navigator); in
            // plain exploration the navigator stands down and this fires instead — the game's pause toggle,
            // matching the game's own Space binding. (A move queued while paused only walks once unpaused.)
            InputManager.Register("focus.tooltip", "Read tooltip / toggle pause",
                TogglePauseIfExploring).AddBinding(KeyCode.Space);
            InputManager.Register("nav.next", "Next (Tab)").AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("nav.prev", "Previous (Shift+Tab)").AddBinding(KeyCode.Tab, shift: true).Repeating();
            // Ctrl+Up/Down jump between regions of a FlowSheet (handled by the navigator only when focus is
            // inside one; unbound elsewhere in UI screens, so no collision).
            InputManager.Register("nav.regionPrev", "Previous region").AddBinding(KeyCode.UpArrow, ctrl: true).Repeating();
            InputManager.Register("nav.regionNext", "Next region").AddBinding(KeyCode.DownArrow, ctrl: true).Repeating();
            // Mod menu — Ctrl+M, available everywhere (a global hotkey, fires in either focus mode).
            InputManager.Register("mod.menu", "Open mod menu",
                () => WrathAccess.Screens.ModMenuScreen.Toggle()).AddBinding(KeyCode.M, ctrl: true);

            // Ctrl+T: toggle the game's combat mode (real-time-with-pause <-> turn-based). Ctrl+T is free in
            // normal play (the game's Ctrl+T "LocalTeleport" is a cheat-only binding).
            InputManager.Register("combat.toggleMode", "Toggle turn-based / real-time",
                ToggleCombatMode).AddBinding(KeyCode.T, ctrl: true);

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
            // Turn-based status: the acting unit's action economy + remaining movement (self-gates like
            // the scanner keys).
            InputManager.Register("combat.status", "Combat status: actions and movement",
                WrathAccess.Exploration.CombatMode.AnnounceStatus).AddBinding(KeyCode.R);
            InputManager.Register("scan.interact", "Scanner: interact with item",
                WrathAccess.Exploration.Scanner.InteractSelected).AddBinding(KeyCode.I);
            InputManager.Register("scan.moveToCursor", "Scanner: move to cursor",
                WrathAccess.Exploration.Scanner.MoveToCursor).AddBinding(KeyCode.Backspace);
            InputManager.Register("scan.debugShowAll", "Scanner: toggle show all (debug)",
                WrathAccess.Exploration.Scanner.ToggleDebugShowAll).AddBinding(KeyCode.F11);
            InputManager.Register("scan.debugDumpNames", "Scanner: dump object names to log (debug)",
                WrathAccess.Exploration.Scanner.DumpObjectNames).AddBinding(KeyCode.F10);

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
