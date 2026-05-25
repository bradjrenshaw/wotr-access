using System;
using Kingmaker;

namespace WrathAccess
{
    /// <summary>
    /// When active, holds the game's <c>KeyboardAccess.Disabled</c> guard so the
    /// game's own keyboard shortcuts are suppressed and our navigation owns the
    /// keyboard. <c>KeyboardAccess.Tick()</c> early-returns while Disabled is held,
    /// so this is the game's own, fully reversible "mute my shortcuts" lever.
    ///
    /// Note: this only mutes KeyboardAccess hotkeys — not Rewired movement/camera
    /// (those are inert in full-screen UI anyway; handled separately on the map).
    /// Our own keys are captured via InputManager's poll, independent of this guard.
    /// </summary>
    public static class FocusMode
    {
        private static IDisposable _guard;

        public static bool Active => _guard != null;

        public static void Toggle() => Set(!Active);

        public static void Set(bool on)
        {
            if (on == Active) return;
            if (on)
            {
                // Game/Keyboard may not exist extremely early; if so, this no-ops.
                _guard = Game.Instance?.Keyboard?.Disabled.Scope();
                if (_guard == null)
                    Main.Log?.Log("FocusMode: could not engage (game not ready).");
            }
            else
            {
                _guard?.Dispose();
                _guard = null;
            }
        }
    }
}
