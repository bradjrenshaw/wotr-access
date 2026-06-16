using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.GameModes; // GameModeType
using Kingmaker.Modding; // OwlcatModification (the game's native mod system)
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays;
using WrathAccess.Input;
using WrathAccess.Screens;
using WrathAccess.UI; // NavDirection

namespace WrathAccess
{
    /// <summary>
    /// Entry point for the game's NATIVE mod system (Kingmaker.Modding — no Unity Mod Manager). The game
    /// loads every assembly under <c>Modifications/WrathAccess/Assemblies/</c> and invokes the
    /// <c>[OwlcatModificationEnterPoint]</c> method during boot (GameStarter, before the main menu),
    /// passing our <c>OwlcatModification</c>. Install = copy the mod folder + list "WrathAccess" in
    /// <c>OwlcatModificationManagerSettings.json</c> (+ prism.dll next to Wrath.exe) — pure file
    /// operations, no third-party installer. The native system has no per-frame hook, so we spawn our own
    /// persistent <see cref="Ticker"/> MonoBehaviour to drive the input/screen/overlay loops.
    /// </summary>
    public static class Main
    {
        public static ModLogger Log;

        /// <summary>Master switch, flipped from the game's Modifications window (OnSetEnabled).</summary>
        public static bool Enabled = true;

        /// <summary>The mod's install folder (assets live at its root, the DLL under Assemblies/).</summary>
        public static string ModDir { get; private set; }

        private static Harmony _harmony;

        [OwlcatModificationEnterPoint]
        public static void Load(OwlcatModification modification)
        {
            Log = new ModLogger();
            ModDir = modification.Path;
            // Wire the game's enable/disable plumbing to our master switch.
            modification.IsEnabled = () => Enabled;
            modification.OnSetEnabled = enabled =>
            {
                Enabled = enabled;
                if (!enabled) FocusMode.Set(false);
                Log.Log("WrathAccess " + (enabled ? "enabled" : "disabled"));
            };

            try
            {
                WrathAccess.Localization.LocalizationManager.Initialize(); // wire Message's resolver early
                _harmony = new Harmony("WrathAccess");
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
                // Speech comes up AFTER settings load so the persisted handler/backend choices apply.
                WrathAccess.Speech.SpeechManager.Initialize();
                // Overlays are built AFTER load: the saved overlay-id list (incl. user-added ones) is only
                // known once settings have loaded, then their saved values are re-applied to the new subtrees.
                WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.BuildOverlays();
                // Build the saved additional speech configs + re-apply their values (same post-load pattern).
                WrathAccess.Speech.SpeechConfigRegistry.BuildConfigs();
                // Event settings (built after the config roster so the speech-config pickers list them).
                WrathAccess.Events.EventRegistry.RegisterDefaults();
                ScreenManager.Initialize();
                // Game-log reading (barks, rolls, loot, …) lives in the overlays' Log system — see
                // Overlays.LogSystem (per-overlay, per-message-type toggles) fed by the LogFeed Harmony tap.
                WarningReader.Initialize(); // speak the game's "can't do that" warnings (refusal reasons)
                WrathAccess.Events.EventBusAdapter.Initialize(); // turn game damage/buff events into mod events

                // The native mod system has no per-frame callback — drive our loops from our own
                // persistent MonoBehaviour (survives scene loads via DontDestroyOnLoad).
                var ticker = new GameObject("WrathAccess.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.AddComponent<Ticker>();
                // The virtual audio head (+10000: its LateUpdate must land AFTER the game's camera
                // snap so the listener override wins while active).
                ticker.AddComponent<WrathAccess.Exploration.ListenerAnchor>();

                Log.Log("WrathAccess initialized. " + BuildStamp());
                Tts.Speak(Loc.T("app.loaded"));
            }
            catch (Exception e)
            {
                Log.Error("Initialization failed: " + e);
            }
        }

        // The loaded DLL's build time + path, logged at startup so we can confirm from Player.log which
        // build is actually running (the game loads the assembly at boot — code changes need a restart).
        private static string BuildStamp()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return "Build " + System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") + " (" + path + ")";
            }
            catch { return "Build ?"; }
        }

        /// <summary>Our per-frame driver (the native mod system has no update hook of its own). The
        /// negative execution order runs us BEFORE the game's scripts each frame — UMM's dispatcher got
        /// that position implicitly by being created at injection time, before any game object existed;
        /// created mid-boot we'd otherwise run after them, adding a frame of input latency.</summary>
        [DefaultExecutionOrder(-10000)]
        private sealed class Ticker : MonoBehaviour
        {
            private void Update() { try { OnFrame(); } catch (Exception e) { Log?.Error("[tick] " + e); } }
        }

        // Focus mode starts ON (no hotkey ritual every launch) — but the game's Keyboard doesn't exist
        // yet at our entry point (GameStarter), so engage on the first frame it does. One-shot: a later
        // manual toggle-off stays off.
        private static bool _bootFocusPending = true;

        private static void OnFrame()
        {
            if (!Enabled) return;
            if (_bootFocusPending && Game.Instance?.Keyboard != null)
            {
                _bootFocusPending = false;
                FocusMode.Set(true); // silent: the first screen announcement signals we're live
            }
            WrathAccess.Localization.LocalizationManager.Tick(); // pick up a live game-language swap
            FocusMode.Tick(); // re-acquire the hotkey-suppression scope if the game rebuilt its keyboard
            WrathAccess.Audio.WwiseAudio.Tick(); // generate+load our Wwise bank once the engine is up
            InputManager.Tick();
            ScreenManager.Tick();
            WrathAccess.UI.Navigation.TickTypeahead(); // typed letters → type-ahead search (after dispatch)
            TickPause(); // announce the game's pause state whenever it changes (ours OR the game's own)
            TickControl(); // chime when a cutscene/scripted event takes or returns control of the party
            WrathAccess.Exploration.CombatMode.TickTurn(); // announce whose turn it is in turn-based combat
            WrathAccess.Exploration.WorldModel.Tick(); // refresh the area entity registry before consumers read it
            WrathAccess.Exploration.RoomMap.Tick(); // (re)build the room segmentation on area-part change
            WrathAccess.Exploration.RestAction.Tick(); // finish a pending rest: interact with the freshly spawned camp
            // Unscaled delta: the cursor is a real-time UI element — it must keep moving while the game is
            // paused (the game-scaled dt is 0 when paused, which froze continuous-mode movement).
            // Ticks the active overlay: movement modes (glide) update the cursor, then systems (sonar,
            // wall tones, fog/object cues) read the fresh position.
            OverlayManager.Tick(UnityEngine.Time.unscaledDeltaTime);
            WrathAccess.Events.EventBusAdapter.Tick(); // reconcile this frame's buff churn into gain/loss events
            WrathAccess.Events.EventDispatcher.Tick(); // flush this frame's queued events (damage, buffs, room changes)
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
                Tts.Speak(Loc.T(paused ? "pause.paused" : "pause.unpaused"));
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
            if (!QuickSaveModes.Contains(game.CurrentMode)) { Tts.Speak(Loc.T("save.cant")); return; }
            try { game.MakeQuickSave(); Tts.Speak(Loc.T("save.saving")); }
            catch (Exception e) { Log.Error("[quicksave] " + e); Tts.Speak(Loc.T("save.failed")); }
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
            Tts.Speak(Message.Localized("ui", nowTurnBased ? "combat.mode_turn_based" : "combat.mode_real_time").Resolve());
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
            // One collapsible group per input category - the same chord may legitimately appear in two
            // groups (stack-order shadowing resolves it); duplicates are only prevented WITHIN a group.
            var inputCats = new[]
            {
                (InputCategory.Global, "global", "Global"),
                (InputCategory.UI, "ui", "Menus and UI"),
                (InputCategory.Exploration, "explore", "Exploration"),
            };
            foreach (var (cat, key, label) in inputCats)
            {
                var catGroup = new WrathAccess.Settings.CategorySetting(key, label, localizationKey: "input." + key);
                // Ungrouped actions sit at the category root; grouped ones nest in named sub-trees
                // (cursor / party / combat / scanner / overlays). Display order: the sub-trees first
                // (alphabetical by localized label), then the root rebind rows (alphabetical too).
                var leaves = new System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>();
                var subGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>>();
                foreach (var a in InputManager.Actions)
                {
                    if (a.Category != cat) continue;
                    var row = new WrathAccess.Settings.BindingSetting(a);
                    if (a.Group == null) { leaves.Add(row); continue; }
                    if (!subGroups.TryGetValue(a.Group, out var rows))
                        subGroups[a.Group] = rows = new System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>();
                    rows.Add(row);
                }
                foreach (var g in System.Linq.Enumerable.OrderBy(subGroups.Keys,
                    k => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", "input.group." + k, k),
                    StringComparer.CurrentCultureIgnoreCase))
                {
                    var sub = new WrathAccess.Settings.CategorySetting(g, g, localizationKey: "input.group." + g);
                    foreach (var b in System.Linq.Enumerable.OrderBy(subGroups[g], r => r.Label, StringComparer.CurrentCultureIgnoreCase))
                        sub.Add(b);
                    catGroup.Add(sub);
                }
                foreach (var b in System.Linq.Enumerable.OrderBy(leaves, r => r.Label, StringComparer.CurrentCultureIgnoreCase))
                    catGroup.Add(b);
                bindings.Add(catGroup);
            }
            WrathAccess.Settings.ModSettings.Root.Add(bindings);

            // UI = per-announcement settings (global toggles) + per-element-type overrides, discovered
            // by reflection. Creates "announcements" + "ui" categories under the settings Root.
            WrathAccess.UI.Announcements.AnnouncementRegistry.RegisterDefaults();

            // Audio = settings-wide master volume (every overlay sound system scales by it).
            var audio = new WrathAccess.Settings.CategorySetting("audio", "Audio", localizationKey: "category.audio");
            audio.Add(new WrathAccess.Settings.IntSetting("master_volume", "Master volume", 100, 0, 100, 5, "audio.master_volume"));
            WrathAccess.Settings.ModSettings.Root.Add(audio);
            // The shared sonar-sound taxonomy (global, like the volumes): per-node sound picks the
            // sonar/object cues resolve live. Shown on the Sonar tab.
            WrathAccess.Exploration.SonarTaxonomy.RegisterSettings();

            // Overlays = the data-driven area-overlay configs (composition per overlay + shared defaults); also
            // builds the live Overlay objects and installs them in OverlayManager.
            WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Register();

            // Speech tab: the handler dropdown (Prism / SAPI / Clipboard, auto by default) + each
            // handler's own settings subtree, all rendered by the settings treeview.
            var speech = new WrathAccess.Settings.CategorySetting("speech", "Speech", localizationKey: "category.speech");
            WrathAccess.Speech.SpeechManager.RegisterSettings(speech);
            WrathAccess.Settings.ModSettings.Root.Add(speech);
            // The additional-speech-config roster (advanced; the events system speaks through these).
            // Pre-load like overlays: create the id list now so Load restores it; subtrees built after.
            WrathAccess.Speech.SpeechConfigRegistry.Register();
        }

        private static void RegisterInput()
        {
            // ---- Global: always live, even when focus mode is off (handlers self-gate as needed) ----
            InputManager.Register("toggle_focus", "Toggle focus mode", InputCategory.Global, () =>
            {
                FocusMode.Toggle();
                Tts.Speak(Loc.T(FocusMode.Active ? "focus.on" : "focus.off"));
                if (FocusMode.Active) WrathAccess.UI.Navigation.AnnounceCurrent();
            }).AddBinding(KeyCode.A, ctrl: true, shift: true);

            InputManager.Register("speak_test", "Speak test", InputCategory.Global, () =>
                Tts.Speak("Wrath Access input and speech are working."))
                .AddBinding(KeyCode.T, ctrl: true, shift: true);

            // Quick save (the game's own MakeQuickSave). Self-gates to focus mode + a save-allowed mode.
            InputManager.Register("game.quickSave", "Quick save", InputCategory.Global, QuickSave)
                .AddBinding(KeyCode.F5);

            // Mod menu - Ctrl+M, available everywhere (fires in either focus mode).
            InputManager.Register("mod.menu", "Open mod menu", InputCategory.Global,
                () => WrathAccess.Screens.ModMenuScreen.Toggle()).AddBinding(KeyCode.M, ctrl: true);

            // ---- UI: screen/menu navigation (dispatched into the active navigator) ----
            InputManager.Register("ui.up", "Navigate up", InputCategory.UI).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("ui.down", "Navigate down", InputCategory.UI).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("ui.left", "Navigate left", InputCategory.UI).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("ui.right", "Navigate right", InputCategory.UI).AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("ui.next", "Next region (Tab)", InputCategory.UI).AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("ui.prev", "Previous region (Shift+Tab)", InputCategory.UI).AddBinding(KeyCode.Tab, shift: true).Repeating();
            InputManager.Register("ui.activate", "Activate control", InputCategory.UI)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("ui.secondary", "Secondary action", InputCategory.UI).AddBinding(KeyCode.Backspace);
            InputManager.Register("ui.back", "Back / close", InputCategory.UI).AddBinding(KeyCode.Escape);
            // Space + F1 (one action, two bindings): while a type-ahead search is live, Space extends
            // the search buffer (the SPACE key is reserved, not the action), so F1 still reads tooltips.
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI)
                .AddBinding(KeyCode.Space).AddBinding(KeyCode.F1);
            // Home/End jump to the first/last item: a list's ends, a tree's current depth's ends, a
            // FlowSheet's very first/last cell regardless of region (and a live search's first/last hit).
            InputManager.Register("ui.home", "Jump to first item", InputCategory.UI).AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Jump to last item", InputCategory.UI).AddBinding(KeyCode.End);
            // Ctrl+Up/Down jump between regions of a FlowSheet (handled only when focus is inside one).
            InputManager.Register("ui.regionPrev", "Previous sheet region", InputCategory.UI)
                .AddBinding(KeyCode.UpArrow, ctrl: true).Repeating();
            InputManager.Register("ui.regionNext", "Next sheet region", InputCategory.UI)
                .AddBinding(KeyCode.DownArrow, ctrl: true).Repeating();

            // ---- Exploration: the in-game world (live in ctx.ingame; shared chords - arrows, Enter,
            // Space, Escape, Backspace - are won by the HUD while it's focused, by these while not) ----
            // The cursor arrows have NO press handlers: movement modes POLL the held keys each frame as
            // one combined vector (CursorKeys), so held diagonals move diagonally instead of zigzagging.
            InputManager.Register("explore.cursorUp", "Move cursor up", InputCategory.Exploration)
                .AddBinding(KeyCode.UpArrow).AddBinding(KeyCode.W).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorDown", "Move cursor down", InputCategory.Exploration)
                .AddBinding(KeyCode.DownArrow).AddBinding(KeyCode.S).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorLeft", "Move cursor left", InputCategory.Exploration)
                .AddBinding(KeyCode.LeftArrow).AddBinding(KeyCode.A).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorRight", "Move cursor right", InputCategory.Exploration)
                .AddBinding(KeyCode.RightArrow).AddBinding(KeyCode.D).Repeating().Grouped("cursor");
            InputManager.Register("explore.secondaryUp", "Secondary cursor up", InputCategory.Exploration)
                .AddBinding(KeyCode.UpArrow, shift: true).AddBinding(KeyCode.W, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryDown", "Secondary cursor down", InputCategory.Exploration)
                .AddBinding(KeyCode.DownArrow, shift: true).AddBinding(KeyCode.S, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryLeft", "Secondary cursor left", InputCategory.Exploration)
                .AddBinding(KeyCode.LeftArrow, shift: true).AddBinding(KeyCode.A, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryRight", "Secondary cursor right", InputCategory.Exploration)
                .AddBinding(KeyCode.RightArrow, shift: true).AddBinding(KeyCode.D, shift: true).Grouped("cursor");
            // Our "left click": interact with the thing under the cursor.
            InputManager.Register("explore.interact", "Interact at cursor", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.InteractAtCursor)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            // The game's pause toggle, matching its own Space binding. (A move queued while paused only
            // walks once unpaused.)
            InputManager.Register("explore.pause", "Toggle pause", InputCategory.Exploration,
                TogglePauseIfExploring).AddBinding(KeyCode.Space);
            // Y: "where am I" — the location's name (current section when it has one), indoors, and the
            // leader's compass region within the section's map bounds.
            InputManager.Register("explore.whereAmI", "Where am I", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceWhereAmI).AddBinding(KeyCode.Y);
            InputManager.Register("explore.cancelTargeting", "Cancel targeting / game menu", InputCategory.Exploration,
                () =>
                {
                    if (WrathAccess.Exploration.Targeting.Aiming) { WrathAccess.Exploration.Targeting.Cancel(); return; }
                    // Nothing to cancel → Escape opens the game's pause menu, exactly like the game's
                    // own Esc key (the handler toggles, and EscMenuScreen takes over while it's open).
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                        delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); });
                })
                .AddBinding(KeyCode.Escape);

            // Party selection - drives the game's real selection, which decides move-to-cursor's
            // single-vs-formation behaviour. Ctrl+A = whole party; Ctrl+1..6 = a single member.
            InputManager.Register("party.selectAll", "Select whole party", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.SelectWholeParty).AddBinding(KeyCode.A, ctrl: true).Grouped("party");
            for (int i = 0; i < 6; i++)
            {
                int idx = i; // capture per-iteration for the closure
                InputManager.Register("party.select" + (i + 1), "Select party member " + (i + 1),
                    InputCategory.Exploration, () => WrathAccess.Exploration.PartySelection.SelectMember(idx))
                    .AddBinding(KeyCode.Alpha1 + i, ctrl: true).Grouped("party");
            }

            // Ctrl+T: toggle the game's combat mode (real-time-with-pause <-> turn-based). Ctrl+T is free
            // in normal play (the game's Ctrl+T "LocalTeleport" is a cheat-only binding).
            InputManager.Register("combat.toggleMode", "Toggle turn-based / real-time", InputCategory.Exploration,
                ToggleCombatMode).AddBinding(KeyCode.T, ctrl: true).Grouped("combat");
            // Turn-based status: the acting unit's action economy + remaining movement.
            InputManager.Register("combat.status", "Combat status: actions and movement", InputCategory.Exploration,
                WrathAccess.Exploration.CombatMode.AnnounceStatus).AddBinding(KeyCode.R).Grouped("combat");

            // Exploration scanner: a categorized, distance-sorted list of things in the current area.
            InputManager.Register("scan.itemNext", "Scanner: next item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.NextItem).AddBinding(KeyCode.PageDown).Repeating().Grouped("scanner");
            InputManager.Register("scan.itemPrev", "Scanner: previous item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.PrevItem).AddBinding(KeyCode.PageUp).Repeating().Grouped("scanner");
            InputManager.Register("scan.categoryNext", "Scanner: next category", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.NextCategory).AddBinding(KeyCode.PageDown, ctrl: true).Repeating().Grouped("scanner");
            InputManager.Register("scan.categoryPrev", "Scanner: previous category", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.PrevCategory).AddBinding(KeyCode.PageUp, ctrl: true).Repeating().Grouped("scanner");
            // Home and Slash: plant the movement cursor ON the review target (the explicit opt-in jump).
            InputManager.Register("scan.cursorToItem", "Move cursor to review target", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.CursorToSelected)
                .AddBinding(KeyCode.Home).AddBinding(KeyCode.Slash).Grouped("scanner");
            InputManager.Register("scan.announceCursor", "Announce cursor position", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceCursor).AddBinding(KeyCode.K).Grouped("scanner");
            InputManager.Register("scan.announceParty", "Announce party", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceParty).AddBinding(KeyCode.K, shift: true).Grouped("scanner");
            InputManager.Register("scan.interact", "Scanner: interact with item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.InteractSelected).AddBinding(KeyCode.I).Grouped("scanner");
            InputManager.Register("scan.moveToCursor", "Scanner: move to cursor", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.MoveToCursor).AddBinding(KeyCode.Backspace).Grouped("scanner");
            InputManager.Register("scan.debugShowAll", "Scanner: toggle show all (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.ToggleDebugShowAll).AddBinding(KeyCode.F11).Grouped("scanner");
            InputManager.Register("scan.debugDumpNames", "Scanner: dump object names to log (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DumpObjectNames).AddBinding(KeyCode.F10).Grouped("scanner");
            InputManager.Register("scan.debugAreaParts", "Read area parts (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DebugDumpAreaParts).AddBinding(KeyCode.F9).Grouped("scanner");
            InputManager.Register("scan.debugRooms", "Read room map stats (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.RoomMap.DebugSpeak).AddBinding(KeyCode.F8).Grouped("scanner");

            // Area overlays: swappable spatial views. Arrows drive the active overlay's cursor (see the
            // explore.cursor* actions above).
            InputManager.Register("overlay.cycle", "Cycle area overlay", InputCategory.Exploration,
                OverlayManager.Cycle).AddBinding(KeyCode.O, ctrl: true).Grouped("overlays");
            InputManager.Register("overlay.recenter", "Overlay: recenter on player", InputCategory.Exploration,
                OverlayManager.Recenter).AddBinding(KeyCode.C).Grouped("overlays");
            InputManager.Register("overlay.announce", "Overlay: announce cursor", InputCategory.Exploration,
                OverlayManager.AnnounceCurrent).AddBinding(KeyCode.Keypad5).Grouped("overlays");
            InputManager.Register("overlay.descend", "Overlay: follow surface down", InputCategory.Exploration,
                () => OverlayManager.VerticalFollow(-1)).AddBinding(KeyCode.Period, ctrl: true).Grouped("overlays");
            InputManager.Register("overlay.ascend", "Overlay: follow surface up", InputCategory.Exploration,
                () => OverlayManager.VerticalFollow(1)).AddBinding(KeyCode.Comma, ctrl: true).Grouped("overlays");

            // The review cursor: cycle nearby things by group — closest first from the movement cursor,
            // which NEVER moves (look around while holding position). Shift = cycle backward. The landing
            // becomes the scanner selection, so I interacts with it and Home plants the cursor on it.
            InputManager.Register("review.nextParty", "Review next party member", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Party, 1))
                .AddBinding(KeyCode.Comma).Repeating().Grouped("review");
            InputManager.Register("review.prevParty", "Review previous party member", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Party, -1))
                .AddBinding(KeyCode.Comma, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextEnemy", "Review next enemy", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Enemies, 1))
                .AddBinding(KeyCode.Period).Repeating().Grouped("review");
            InputManager.Register("review.prevEnemy", "Review previous enemy", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Enemies, -1))
                .AddBinding(KeyCode.Period, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextNeutral", "Review next neutral", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Neutrals, 1))
                .AddBinding(KeyCode.N).Repeating().Grouped("review");
            InputManager.Register("review.prevNeutral", "Review previous neutral", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Neutrals, -1))
                .AddBinding(KeyCode.N, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextOther", "Review next object", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Others, 1))
                .AddBinding(KeyCode.M).Repeating().Grouped("review");
            InputManager.Register("review.prevOther", "Review previous object", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Others, -1))
                .AddBinding(KeyCode.M, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextPoi", "Review next point of interest", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Poi, 1))
                .AddBinding(KeyCode.B).Repeating().Grouped("review");
            InputManager.Register("review.prevPoi", "Review previous point of interest", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Poi, -1))
                .AddBinding(KeyCode.B, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextExit", "Review next room exit", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleRoomExits(1))
                .AddBinding(KeyCode.V).Repeating().Grouped("review");
            InputManager.Register("review.prevExit", "Review previous room exit", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleRoomExits(-1))
                .AddBinding(KeyCode.V, shift: true).Repeating().Grouped("review");
        }
    }
}
