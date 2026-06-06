using System.IO;
using System.Reflection;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>Shared audio helpers for the sound systems: the bundled-audio path and the global master
    /// volume (the settings-wide Audio tab), which every system's volume is scaled by.</summary>
    internal static class OverlayAudio
    {
        public static string Dir =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");

        /// <summary>Global master volume as a 0..1 fraction (default full).</summary>
        public static float Master =>
            (ModSettings.GetSetting<IntSetting>("audio.master_volume")?.Get() ?? 100) / 100f;
    }
}
