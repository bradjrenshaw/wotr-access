using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The one virtual cursor — a single world point that the scanner and every overlay share. The
    /// scanner plants it on a listed thing (Home); the tile-view overlay walks it across the grid; and
    /// move-to-cursor (Backspace) sends the party wherever it currently sits, no matter which tool put it
    /// there. Keeping it shared is what lets you browse tiles and then walk to the one you're on.
    /// </summary>
    internal static class Cursor
    {
        private static Vector3? _raw;
        private static System.Func<Vector3?> _provider;

        /// <summary>The point every consumer reads. While an override provider is registered (the map
        /// screen), READS come from it — sonar, wall tones, cues and the audio listener all follow the
        /// map cursor with no per-system plumbing — while the underlying in-area point keeps its value
        /// untouched, so closing the map snaps everything back to where you were exploring. Writes
        /// (<see cref="Set"/>) always target the real in-area point (that's what the map screen's
        /// Enter does: plant the world cursor from under the override).</summary>
        public static Vector3? Position => _provider != null ? _provider() : _raw;
        public static bool Has => Position.HasValue;
        public static void Set(Vector3 p) => _raw = p;
        public static void Clear() => _raw = null;

        public static void SetOverride(System.Func<Vector3?> provider) => _provider = provider;
        public static void ClearOverride() => _provider = null;
    }
}
