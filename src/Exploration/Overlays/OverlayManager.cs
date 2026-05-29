using System.Collections.Generic;
using WrathAccess.Screens;
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Holds the registered area overlays and which one is active, and forwards input to it. Cycling steps
    /// off → overlay 0 → … → off, and only while exploring (focus mode owns the keyboard AND the plain
    /// in-game context is on top), so the overlay keys never fight the game during normal play. Movement
    /// verbs are gated on <see cref="Active"/>, so they're inert unless an overlay is actually up.
    /// </summary>
    internal static class OverlayManager
    {
        private static readonly List<Overlay> _overlays = new List<Overlay> { new VirtualTileView() };
        private static int _active = -1; // -1 = overlays off

        private static bool InExploration =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        public static bool Active => InExploration && _active >= 0;

        private static Overlay Current => _active >= 0 ? _overlays[_active] : null;

        public static void Cycle()
        {
            if (!InExploration) return;
            Current?.OnExit();
            _active++;
            if (_active >= _overlays.Count) _active = -1;
            if (Current == null) { Tts.Speak("Overlays off", interrupt: true); return; }
            Tts.Speak(Current.Name, interrupt: true);
            Current.OnEnter();
        }

        public static void Move(NavDirection dir) { if (Active) Current.Move(dir); }
        public static void VerticalFollow(int dir) { if (Active) Current.VerticalFollow(dir); }
        public static void Recenter() { if (Active) Current.Recenter(); }
        public static void AnnounceCurrent() { if (Active) Current.AnnounceCurrent(); }
    }
}
