using System.IO;
using System.Reflection;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>Shared path to the mod's bundled audio (<c>assets/audio</c>), used by the sound systems.</summary>
    internal static class OverlayAudio
    {
        public static string Dir =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");
    }
}
