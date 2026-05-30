using System.IO;
using System.Reflection;
using Kingmaker;
using Kingmaker.Controllers; // FogOfWarController
using WrathAccess.Audio;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Audio sonification of the surroundings. First slice: plays a fog-enter / fog-exit one-shot when the
    /// shared <see cref="Cursor"/> crosses the fog-of-war boundary (`FogOfWarController.IsInFogOfWar` —
    /// party-vision "currently lit", see [[wotr-exploration-world-model]]). Ticked after the overlays so it
    /// sees the cursor's new position this frame. Will grow into per-entity sonification over
    /// <see cref="WorldModel"/> (subscribe to Added/Removed, sound per item).
    /// </summary>
    internal static class Sonar
    {
        private static readonly SfxPlayer Sfx = new SfxPlayer();
        private static bool? _wasFogged; // null = no baseline yet (don't fire on the first sample)

        private static bool InExploration =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        private static string AudioDir =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");

        public static void Tick()
        {
            if (!InExploration || !Cursor.Has) { _wasFogged = null; return; }

            bool fogged = FogOfWarController.IsInFogOfWar(Cursor.Position.Value);
            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
                Sfx.Play(Path.Combine(AudioDir, fogged ? "fog_enter.wav" : "fog_exit.wav"));
            _wasFogged = fogged;
        }
    }
}
