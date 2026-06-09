using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Turn-based path feedback: the instant the cursor stops after moving (movement keys released, position
    /// no longer changing), speak whether the acting unit has a path to that spot and how long the walk is —
    /// "Path, 25 feet" (+ "beyond remaining movement" when it overruns this turn's budget), or "No path" when
    /// the spot is unreachable (the pathfinder only gets a partial route toward it). Armed by a small cursor
    /// delta and fired once per stop, so an idle cursor never repeats; silent outside turn-based combat. Uses
    /// the game's own pathfinder via <see cref="CombatMode.TryPathInfo"/> — the same path a move would walk.
    /// </summary>
    internal sealed class PathInfoSystem : OverlaySystem
    {
        public override string Name => "Path info";
        public override string Key => "path";

        private Vector3 _last;
        private bool _has;     // _last is valid
        private bool _armed;   // cursor moved since the last announce

        private const float DeltaSqr = 0.0001f; // arm on a ~0.01-unit move (the cursor never drifts on its own)

        // Reachable means the path actually arrives at the spot; the pathfinder's fallback partial path
        // ends well short. Half a tile of slack covers node snapping.
        private const float ReachToleranceMeters = 1.0f;

        public override void OnExit(Overlay overlay) { _has = false; _armed = false; }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !Enabled || !CombatMode.InTurnBased) { _has = false; _armed = false; return; }
            if (WrathAccess.UI.Navigation.HasFocus) return; // HUD owns the arrows — freeze, don't fire

            var p = overlay.Cursor.Position;
            if (!_has) { _has = true; _last = p; _armed = false; return; }
            if ((p - _last).sqrMagnitude > DeltaSqr) { _last = p; _armed = true; return; }
            if (!_armed || MoveKeyHeld()) return; // not moved yet / still driving
            _armed = false;
            Announce(p);
        }

        private static void Announce(Vector3 dest)
        {
            if (!CombatMode.TryPathInfo(dest, out float len, out float gap, out float moveAction, out float total)
                || gap > ReachToleranceMeters)
            {
                Tts.Speak("No path");
                return;
            }
            int feet = Mathf.RoundToInt(len / Geo.MetresPerFoot);
            string line = "Path, " + feet + (feet == 1 ? " foot" : " feet");
            // Mirror the game's break markers: within the move action → plain; past it but reachable →
            // it costs the standard action too; past everything → not reachable this turn.
            if (len > total + 0.05f) line += ", beyond remaining movement";
            else if (len > moveAction + 0.05f) line += ", uses standard action";
            Tts.Speak(line);
        }

        private static bool MoveKeyHeld()
            => InputManager.Held("nav.up") || InputManager.Held("nav.down")
            || InputManager.Held("nav.left") || InputManager.Held("nav.right");
    }
}
