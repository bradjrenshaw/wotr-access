using Kingmaker;

namespace WrathAccess
{
    /// <summary>
    /// Single source of truth for whether the player can act in the world. We use the GAME's own input
    /// gate: <c>Game.ClickEventsController</c> (the world-click <c>PointerController</c>) is assigned per
    /// game mode — set in Default/Pause, GlobalMap/Kingdom/Settlement and TacticalCombat, and left null in
    /// everything else (Cutscene, Dialog incl. storybook/interchapter, Rest, FullScreenUi menus, loading /
    /// None). So "the click controller exists" == "the player has control". One field read the game
    /// maintains on every mode change — no scene lookup, no flag conjunction to flicker or get stuck.
    /// </summary>
    internal static class ControlState
    {
        public static bool HasControl => Game.Instance?.ClickEventsController != null;

        // How long the raw state must hold before Settled adopts it. Cutscenes blip the click
        // controller for a frame or two between beats; this rides those out. Short by design —
        // the isolated cutscene start/stop stutters are gone, and a long settle reads as lag on
        // the control chime and on event speech resuming.
        public const float SettleSeconds = 0.15f;

        private static bool _settled = true, _pending = true;
        private static float _pendingSince;

        /// <summary>The DEBOUNCED control state: <see cref="HasControl"/> held for
        /// <see cref="SettleSeconds"/>. Consumers that must not react to the between-beat blips
        /// (the control chime, buff-event gating) read this; instant gating (input categories,
        /// cursor movement) reads the raw property.</summary>
        public static bool Settled => _settled;

        /// <summary>Advance the debounce; call once per frame, before the consumers of <see cref="Settled"/>.</summary>
        public static void Tick()
        {
            bool raw = HasControl;
            float now = UnityEngine.Time.unscaledTime;
            if (raw != _pending) { _pending = raw; _pendingSince = now; }
            if (_settled != _pending && now - _pendingSince >= SettleSeconds) _settled = _pending;
        }
    }
}
