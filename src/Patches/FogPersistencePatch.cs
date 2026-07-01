using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Area;
using Kingmaker.EntitySystem.Persistence;
using WrathAccess.Exploration;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Persists explored-terrain (see <see cref="FogExplored"/>) INSIDE the game save, as our own entry — WotR
    /// keeps no explored layer of its own, and the save (a .zks) is just an Ionic.Zip archive of named entries
    /// (header/party/area json, .fog masks). We add one more, "wrathaccess_explored", so it rides the save +
    /// Steam Cloud with zero coupling to any game data structure: we touch only the public <see cref="ISaver"/>
    /// entry API and an entry name we invented. If a patch ever changed the save internals (it won't — frozen),
    /// the worst case is our entry silently doesn't round-trip and we fall back to in-session accumulation.
    /// </summary>
    [HarmonyPatch]
    internal static class FogSavePatch
    {
        // ZipSaver is internal; resolve its Save() (the finalize called after every entry is added).
        private static MethodBase TargetMethod()
            => AccessTools.Method("Kingmaker.EntitySystem.Persistence.ZipSaver:Save");

        private static void Prefix(object __instance)
        {
            try
            {
                var blob = FogExplored.Serialize();
                if (blob != null && blob.Length > 0)
                    ((ISaver)__instance).SaveBytes("wrathaccess_explored", blob); // one more entry, then the zip writes
            }
            catch (Exception e) { Main.Log?.Error("[fog] save write: " + e); }
        }
    }

    /// <summary>Every real load path funnels through <c>Game.LoadGame(SaveInfo)</c> (QuickLoad + main-menu load
    /// both call it); its prefix runs synchronously before the area loads. We read our entry from the save's
    /// .zks (via a reflectively-constructed <c>ZipSaver</c> — the game's own reader, public ctor + ISaver) and
    /// restore the grids, so they're in place before the area binds. A missing entry / folder-save just clears.</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    internal static class FogLoadPatch
    {
        private static void Prefix(SaveInfo saveInfo)
        {
            try
            {
                if (saveInfo == null || string.IsNullOrEmpty(saveInfo.FolderName)) { FogExplored.Restore(null); return; }
                var t = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.ZipSaver");
                byte[] blob = null;
                using (var saver = (ISaver)Activator.CreateInstance(t, saveInfo.FolderName))
                    blob = saver.ReadBytes("wrathaccess_explored");
                FogExplored.Restore(blob);
            }
            catch (Exception e) { Main.Log?.Error("[fog] load restore: " + e); FogExplored.Restore(null); }
        }
    }

    /// <summary>A new game must not inherit a previously-loaded save's grids (they'd wrongly restore by scene
    /// name). LoadNewGame() routes through this (preset, importFrom) overload, so clearing here covers both.</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadNewGame), new[] { typeof(BlueprintAreaPreset), typeof(SaveInfo) })]
    internal static class FogNewGamePatch
    {
        private static void Prefix() => FogExplored.Clear();
    }
}
