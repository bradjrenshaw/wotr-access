using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The live <see cref="ServiceWindowsVM"/>, from whichever ROOT currently owns the service
    /// windows: in an area it's the InGame static part; on the WORLD MAP it's the GlobalMapVM
    /// (each constructs its own instance). Every service-window screen resolves through here —
    /// reading only the in-game chain made a window opened on the world map an EMPTY screen whose
    /// Escape closed nothing (the Ctrl+I-on-the-world-map softlock).
    /// </summary>
    internal static class ServiceWindows
    {
        public static ServiceWindowsVM Current
        {
            get
            {
                var rc = Game.Instance?.RootUiContext;
                if (rc == null) return null;
                return rc.InGameVM?.StaticPartVM?.ServiceWindowsVM ?? rc.GlobalMapVM?.ServiceWindowsVM;
            }
        }
    }
}
