#if DEBUG
using System;
using System.Text;
using System.Threading;
using WrathAccess.Input;
using WrathAccess.UI;

namespace WrathAccess.Dev
{
    /// <summary>
    /// The MODULE's routes on the host's dev server — everything that calls module types (/gui,
    /// /input, /loadsave) plus the /speech tap wiring. Registered from the module's Load, removed
    /// in its Dispose, so a hot-reload swaps the handlers along with the code they call.
    /// </summary>
    internal static class DevModuleRoutes
    {
        public static void Register()
        {
            var s = DevServer.Instance;
            WrathAccess.Speech.SpeechManager.Observer = s.TapSpeech;
            s.RegisterRoute("/gui", (method, body, query) => s.OnMain(() => GuiInspector.Dump()));
            s.RegisterRoute("/input", (method, body, query) =>
            {
                string verb = (body ?? "").Trim();
                return s.OnMain(() => Inject(verb));
            });
            s.RegisterRoute("/loadsave", (method, body, query) => LoadSave(body));
        }

        public static void Unregister()
        {
            var s = DevServer.Instance;
            WrathAccess.Speech.SpeechManager.Observer = null;
            s.UnregisterRoute("/gui");
            s.UnregisterRoute("/input");
            s.UnregisterRoute("/loadsave");
        }

        // Fire one of our InputActions by key, exactly as InputManager.Tick routes a real press: a UI action
        // goes to the navigator; anything else fires its handler. Lets the dev driver drive nav (ui.down,
        // ui.activate, ui.next…) and global hotkeys. Unknown key → list what's available. Main-thread only.
        private static string Inject(string key)
        {
            foreach (var a in InputManager.Actions)
            {
                if (a.Key != key) continue;
                bool consumed = a.Category == InputCategory.UI && Navigation.DispatchJustPressed(a);
                if (!consumed) a.InvokePerformed();
                return "fired " + key + (consumed ? " (navigator)" : " (handler)") + "\n";
            }
            var sb = new StringBuilder("[unknown action] " + key + "\navailable:\n");
            foreach (var a in InputManager.Actions) sb.Append("  ").Append(a.Key).Append('\n');
            return sb.ToString();
        }

        // Load a save from the main menu and BLOCK until the gameplay scene is interactive, so the driver
        // can script "drop me in-game" in one call. body = "latest" (default) | "quick" | "area:<Blueprint>"
        // | an index into the save list. Drives the CONTINUE-button path, then polls for a play context.
        private static string LoadSave(string body)
        {
            var srv = DevServer.Instance;
            string sel = (body ?? "").Trim();
            if (sel.Length == 0) sel = "latest";

            string kick = srv.OnMain(() =>
            {
                var game = Kingmaker.Game.Instance;
                if (game == null || game.SaveManager == null) return "[not ready] no SaveManager yet; retry\n";
                var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
                if (lp == null || lp.IsLoadingScreenActive) return "[not ready] still on a loading screen; retry\n";
                var mm = game.UI?.MainMenu;
                if (mm == null) return "[not ready] not at the main menu (load only from the title screen); retry\n";
                var save = ResolveSave(game.SaveManager, sel);
                if (save == null) return "[no save] '" + sel + "' not found (saves still loading? retry)\n";
                // The real Continue-button path: MainMenu.LoadGame wraps the load in EnterGame (loading
                // screen, menu teardown, obligatory scenes) — calling LoadGameFromMainMenu directly
                // leaves a broken half-load.
                mm.LoadGame(save);
                return "ok\n";
            });
            if (kick != "ok\n") return kick;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 90)
            {
                string status = srv.OnMain(() =>
                {
                    var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
                    if (lp != null && lp.IsLoadingScreenActive) return "";
                    string key = WrathAccess.Screens.ScreenManager.Current?.Key;
                    bool inPlay = key == "ctx.ingame" || key == "ctx.tacticalcombat" || key == "ctx.globalmap";
                    return inPlay ? "loaded '" + sel + "': screen=" + key + "\n" : "";
                });
                if (status.Length > 0) return status;
                Thread.Sleep(150);
            }
            return "[timeout] load '" + sel + "' did not become interactive within 90s\n";
        }

        private static Kingmaker.EntitySystem.Persistence.SaveInfo ResolveSave(
            Kingmaker.EntitySystem.Persistence.SaveManager mgr, string sel)
        {
            if (sel == "latest") return mgr.GetLatestSave();
            if (sel == "quick") return mgr.GetNewestQuickslot();
            // "area:<BlueprintName>" = the newest save made IN that area (story-correct etude state).
            if (sel.StartsWith("area:", StringComparison.OrdinalIgnoreCase))
            {
                string area = sel.Substring("area:".Length).Trim();
                Kingmaker.EntitySystem.Persistence.SaveInfo best = null;
                foreach (var s in mgr)
                    if (s != null && s.Area != null && s.Area.name == area
                        && (best == null || s.SystemSaveTime > best.SystemSaveTime))
                        best = s;
                return best;
            }
            if (int.TryParse(sel, out int idx))
            {
                int i = 0;
                foreach (var s in mgr) if (i++ == idx) return s;
                return null;
            }
            return mgr.GetLatestSave();
        }
    }
}
#endif
