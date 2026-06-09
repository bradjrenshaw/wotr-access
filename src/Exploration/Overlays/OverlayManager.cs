using System.Collections.Generic;
using WrathAccess.Screens;
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Holds the registered overlays and which one is active, and forwards input to it. Each overlay is a
    /// composition of movement modes + systems (see the builders below). Cycling steps off → 0 → … → off,
    /// only while exploring (focus mode owns the keyboard AND the plain in-game context is on top), so the
    /// overlay keys never fight the game during normal play. Movement/announce verbs are gated on
    /// <see cref="Active"/>; the selected overlay still ticks when inactive so its audio can mute itself.
    /// </summary>
    internal static class OverlayManager
    {
        private static List<Overlay> _overlays = new List<Overlay>();
        private static int _active = -1; // -1 = overlays off

        /// <summary>Install the overlays built from settings (see <see cref="OverlaySettingsRegistry"/>).
        /// Resets selection to off.</summary>
        public static void SetOverlays(List<Overlay> overlays)
        {
            _overlays = overlays ?? new List<Overlay>();
            _active = -1;
        }

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
            if (Current == null) { Tts.Speak(Loc.T("overlay.off"), interrupt: true); return; }
            Tts.Speak(Current.Name, interrupt: true);
            Current.OnEnter();
            Current.AnnounceCurrent();
        }

        // Ticks the selected overlay every frame (whether or not we're exploring); its systems/modes
        // self-gate on Active so they mute/idle when a menu's up or focus is off.
        public static void Tick(float dt) => Current?.Tick(dt);

        public static void Move(MovementSlot slot, NavDirection dir) { if (Active) Current.Move(slot, dir); }
        public static void Recenter() { if (Active) Current.Recenter(); }
        public static void AnnounceCurrent() { if (Active) Current.AnnounceCurrent(); }
        public static void VerticalFollow(int dir) { if (Active) Current.VerticalFollow(dir); }
    }
}
