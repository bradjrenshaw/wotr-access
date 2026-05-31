using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM;
using Kingmaker.UI.MVVM._VM.ServiceWindows;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Resolves the active screen stack from RootUIContext each frame (poll-and-diff,
    /// robust to the VM-recreation lifecycle) and dispatches lifecycle events. The
    /// stack is ordered bottom→top by Layer; Current is the top (the focused screen).
    /// Ticked from Main.OnUpdate.
    /// </summary>
    public static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();

        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public static IReadOnlyList<Screen> Stack => _stack;

        public static void Register(Screen screen) => _registered.Add(screen);

        public static void Tick()
        {
            var next = Resolve();
            Dispatch(_stack, next);
            _stack = next;
            Current?.OnUpdate();
        }

        /// <summary>Active screens, ordered bottom (low layer) → top (high layer).</summary>
        private static List<Screen> Resolve()
        {
            var active = new List<Screen>();
            for (int i = 0; i < _registered.Count; i++)
                if (SafeIsActive(_registered[i])) active.Add(_registered[i]);
            return active.OrderBy(s => s.Layer).ToList();
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                Main.Log?.Error("Screen.IsActive threw for '" + s.Key + "': " + e.Message);
                return false;
            }
        }

        // OnUnfocus(old top) → OnPop(removed, top→bottom) → OnPush(added, bottom→top) → OnFocus(new top).
        private static void Dispatch(List<Screen> oldStack, List<Screen> newStack)
        {
            var oldTop = oldStack.Count > 0 ? oldStack[oldStack.Count - 1] : null;
            var newTop = newStack.Count > 0 ? newStack[newStack.Count - 1] : null;

            if (oldTop != newTop) Safe(() => oldTop?.OnUnfocus(), oldTop, "OnUnfocus");

            for (int i = oldStack.Count - 1; i >= 0; i--)
                if (!newStack.Contains(oldStack[i])) { var s = oldStack[i]; Safe(() => s.OnPop(), s, "OnPop"); }

            for (int i = 0; i < newStack.Count; i++)
                if (!oldStack.Contains(newStack[i])) { var s = newStack[i]; Safe(() => s.OnPush(), s, "OnPush"); }

            if (newTop != oldTop)
            {
                Safe(() => newTop?.OnFocus(), newTop, "OnFocus");
                WrathAccess.UI.Navigation.Attach(newTop); // (re)bind the navigator to the focused screen
                if (WrathAccess.FocusMode.Active) WrathAccess.UI.Navigation.AnnounceCurrent();
            }
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { Main.Log?.Error("Screen." + hook + " threw for '" + (s?.Key ?? "?") + "': " + e); }
        }

        public static void Initialize()
        {
            if (_registered.Count > 0) return;

            // Base contexts (layer 0) — mutually exclusive.
            Register(new MainMenuScreen());
            Register(new NewGameScreen()); // main-menu New Game wizard (shown instead of the sidebar)
            Register(new CharGenScreen()); // chargen / level-up (menu + in-game); layer 15, above contexts
            Register(new PredicateScreen("ctx.ingame", "Game", 0, () => RC()?.IsInGame ?? false));
            Register(new PredicateScreen("ctx.globalmap", "World Map", 0, () => RC()?.IsGlobalMap ?? false));
            Register(new PredicateScreen("ctx.tacticalcombat", "Tactical Combat", 0, () => RC()?.IsTacticalCombat ?? false));
            Register(new DialogueScreen()); // in-game conversation (common DialogVM); layer 15, above contexts + service windows
            Register(new LootScreen()); // loot window (container/corpse); layer 15, above contexts + service windows
            Register(new PredicateScreen("ctx.kingdom", "Kingdom", 0, () => RC()?.IsKingdom ?? false));
            Register(new PredicateScreen("ctx.citybuilder", "City", 0, () => RC()?.IsCityBuilder ?? false));

            // Service windows (layer 10) — one at a time, via CurrentServiceWindow.
            RegisterServiceWindow("Inventory", ServiceWindowsType.Inventory, ServiceWindowsType.Equipment, ServiceWindowsType.SmartItem);
            RegisterServiceWindow("Character", ServiceWindowsType.CharacterInfo);
            RegisterServiceWindow("Mythic Path", ServiceWindowsType.Mythic);
            RegisterServiceWindow("Spellbook", ServiceWindowsType.Spellbook);
            RegisterServiceWindow("Journal", ServiceWindowsType.Journal);
            RegisterServiceWindow("Encyclopedia", ServiceWindowsType.Encyclopedia);
            RegisterServiceWindow("Map", ServiceWindowsType.LocalMap);

            // Overlays (can sit on top of a context/window). Settings lives on the
            // shared CommonVM, so this same screen also covers the in-game pause menu.
            Register(new PredicateScreen("overlay.saveload", "Save and Load", 20, () => RC()?.SaveLoadIsShown ?? false));
            Register(new SettingsScreen());
            Register(new ChoiceSubmenuScreen()); // mod-pushed, layer 26 (above settings)
            Register(new KeyBindCaptureScreen()); // key-binding capture, layer 27 (raw-input passthrough)
            Register(new TutorialScreen()); // modal tutorial popup (movement/camera etc.), layer 28
            Register(new MessageModalScreen()); // generic confirm/message modal, layer 30
            Register(new TooltipScreen()); // on-demand brick-tooltip reader, layer 40 (top)

            Main.Log?.Log("ScreenManager: " + _registered.Count + " screens registered.");
        }

        private static void RegisterServiceWindow(string name, params ServiceWindowsType[] types)
        {
            Register(new PredicateScreen("service." + name, name, 10, () =>
            {
                var rc = RC();
                if (rc == null) return false;
                var cur = rc.CurrentServiceWindow;
                for (int i = 0; i < types.Length; i++)
                    if (cur == types[i]) return true;
                return false;
            }));
        }

        private static RootUIContext RC()
        {
            var g = Game.Instance;
            return g != null ? g.RootUiContext : null;
        }
    }
}
