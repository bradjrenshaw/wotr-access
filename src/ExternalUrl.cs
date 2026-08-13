using System;

namespace WrathAccess
{
    /// <summary>
    /// Opens a URL or document in the user's default browser DETACHED from the game's process tree.
    /// <c>Application.OpenURL</c> spawns the browser as a child of Wrath.exe, and Steam waits for the
    /// game's whole process tree to exit — so docs left open made Steam think the game was still
    /// running and blocked relaunching it (tester repro). Routing the launch through explorer.exe
    /// sidesteps that: the spawned explorer forwards the open request to the user's existing shell
    /// process and exits immediately, so the browser is parented outside the game's tree.
    /// </summary>
    internal static class ExternalUrl
    {
        public static void Open(string url)
        {
            try
            {
                // Two explorer quirks, both live-reproduced via the dev server: it can't parse
                // FORWARD slashes in local paths (Main.ModDir carries them into Path.Combine) and
                // falls back to opening the Documents folder; and added quotes break too. So: no
                // quotes (explorer takes its whole argument tail as one path, spaces included) and
                // backslash-normalized local paths. URLs ("://") keep their slashes.
                var arg = url != null && url.IndexOf("://", StringComparison.Ordinal) < 0
                    ? url.Replace('/', '\\') : url;
                System.Diagnostics.Process.Start("explorer.exe", arg)?.Dispose();
                return;
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[url] detached open failed (" + e.Message + "); falling back to OpenURL.");
            }
            try { UnityEngine.Application.OpenURL(url); } catch { }
        }
    }
}
