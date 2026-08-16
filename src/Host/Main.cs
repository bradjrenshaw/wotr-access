using System;
using Kingmaker.Modding; // OwlcatModification (the game's native mod system)
using UnityEngine;
using WrathAccess.Modularity;

namespace WrathAccess
{
    /// <summary>
    /// The permanent HOST — entry point for the game's NATIVE mod system (Kingmaker.Modding). The
    /// game loads every assembly under <c>Modifications/WrathAccess/Assemblies/</c> and invokes the
    /// <c>[OwlcatModificationEnterPoint]</c> method during boot (GameStarter, before the main menu).
    /// The host owns only what can never hot-reload: this entry, the per-frame <see cref="Ticker"/>,
    /// the dev HTTP server socket, and the <see cref="ModuleLoader"/>. ALL features live in the
    /// byte-loaded module (<c>Module/WrathAccess.Module.dll</c> — deliberately NOT under
    /// Assemblies/, so the Owlcat loader never locks it and a rebuilt copy hot-swaps via
    /// <c>POST /reload</c>). Changing host code still needs a game restart — keep it minimal.
    /// </summary>
    public static class Main
    {
        public static ModLogger Log;

        /// <summary>Master switch, flipped from the game's Modifications window (OnSetEnabled).
        /// The module's Tick observes it (and drops focus mode when it goes false).</summary>
        public static bool Enabled = true;

        /// <summary>The mod's install folder (assets live at its root, the DLL under Assemblies/).</summary>
        public static string ModDir { get; private set; }

        [OwlcatModificationEnterPoint]
        public static void Load(OwlcatModification modification)
        {
            Log = new ModLogger();
            ModDir = modification.Path;
            modification.IsEnabled = () => Enabled;
            modification.OnSetEnabled = enabled =>
            {
                Enabled = enabled;
                Log.Log("WrathAccess " + (enabled ? "enabled" : "disabled"));
            };

            try
            {
                // The native mod system has no per-frame callback — drive the module from our own
                // persistent MonoBehaviour (survives scene loads via DontDestroyOnLoad).
                var ticker = new GameObject("WrathAccess.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.AddComponent<Ticker>();

#if DEBUG
                // Dev-only in-process HTTP driver (eval/speech/reload/health), gated behind
                // WRATHACCESS_DEV=1 / the marker file. Compiled out entirely in Release.
                WrathAccess.Dev.DevServer.Instance.Start();
#endif

                ModuleLoader.LoadInitial(System.IO.Path.Combine(ModDir, "Module", "WrathAccess.Module.dll"));
                Log.Log("WrathAccess host initialized.");
            }
            catch (Exception e)
            {
                Log.Error("Host initialization failed: " + e);
            }
        }

        /// <summary>Our per-frame driver (the native mod system has no update hook of its own). The
        /// negative execution order runs us BEFORE the game's scripts each frame — UMM's dispatcher got
        /// that position implicitly by being created at injection time, before any game object existed;
        /// created mid-boot we'd otherwise run after them, adding a frame of input latency.</summary>
        [DefaultExecutionOrder(-10000)]
        private sealed class Ticker : MonoBehaviour
        {
            private void Update()
            {
#if DEBUG
                // Dev-eval jobs run even when the mod is toggled off, so the driver stays usable.
                try { WrathAccess.Dev.DevServer.Instance.Pump(); } catch (Exception e) { Log?.Error("[pump] " + e); }
#endif
                ModuleLoader.Tick(); // module guards Enabled itself; loader guards exceptions
            }
        }
    }
}
