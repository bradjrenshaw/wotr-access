using Kingmaker; // Game
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.TurnBasedMode; // PathVisualizer, ActionsState
using TurnBased.Controllers; // CombatController, TurnController
using UnityEngine; // Vector3

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Turn-based combat helpers. Commands already execute correctly while it's a unit's turn (the game ticks
    /// the current turn in Default mode); the player just needs to know <i>whose</i> turn it is, since they
    /// can't see the initiative tracker.
    /// </summary>
    internal static class CombatMode
    {
        public static bool InTurnBased => CombatController.IsInTurnBasedCombat();

        /// <summary>The unit whose turn it is (null between turns).</summary>
        public static UnitEntityData CurrentUnit => CombatController.SelectedUnit;

        // TurnController.GetActionsStates(unit) is private; we need it to drive the game's command pathfinding.
        private static readonly System.Reflection.MethodInfo GetActionsStatesMethod =
            HarmonyLib.AccessTools.Method(typeof(TurnController), "GetActionsStates");

        /// <summary>
        /// Compute, with the GAME's own pathfinding, where the current unit would end up walking toward
        /// <paramref name="point"/> this turn, and return that endpoint (null if no path). We can't rely on
        /// the game's turn-based path preview because it's driven by the OS mouse / hover predictions, which
        /// our keyboard cursor doesn't feed. Instead we point the unit's <c>ActionsState</c> at our cursor and
        /// call <c>PathVisualizer.CalculatePathForCommand</c> — which runs synchronously through the unit's own
        /// agent (<c>AgentASP.FindPath</c> + <c>BlockUntilCalculated</c>) and populates the current path. The
        /// move/attack we then issue walks that path (the turn-based execution clamps it to the budget).
        /// </summary>
        public static Vector3? PathEndpointToward(Vector3 point, float approachRadius = 0.3f)
        {
            var pts = ComputePath(point, approachRadius);
            if (pts == null || pts.Count == 0) return null;
            return pts[pts.Count - 1];
        }

        /// <summary>
        /// Path facts for an announcement: the walk length to <paramref name="point"/>, how far short of it
        /// the path ends (a partial path to the closest reachable node = the spot itself is unreachable),
        /// and the current unit's remaining movement this turn — all in metres. False if no path at all.
        /// </summary>
        public static bool TryPathInfo(Vector3 point, out float lengthMeters, out float endGapMeters,
            out float moveActionMeters, out float totalMeters)
        {
            lengthMeters = endGapMeters = moveActionMeters = totalMeters = 0f;
            var cu = CurrentUnit;
            var pts = ComputePath(point, 0.3f);
            if (cu == null || pts == null || pts.Count == 0) return false;
            for (int i = 1; i < pts.Count; i++) lengthMeters += Vector3.Distance(pts[i - 1], pts[i]);
            var end = pts[pts.Count - 1];
            float dx = end.x - point.x, dz = end.z - point.z;
            endGapMeters = Mathf.Sqrt(dx * dx + dz * dz);
            // Two budgets, matching the game's path break markers: the move action alone, and the maximum
            // with the standard action converted to a second move ("can I get there at all this turn").
            moveActionMeters = cu.CombatState.TBM.GetRemainingMovementRange(total: false, singleActionMove: false);
            totalMeters = cu.CombatState.TBM.GetRemainingMovementRange(total: true, singleActionMove: false);
            return true;
        }

        private static System.Collections.Generic.List<Vector3> ComputePath(Vector3 point, float approachRadius)
        {
            if (!InTurnBased) return null;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            var pv = PathVisualizer.Instance;
            if (turn == null || cu == null || cu.View == null || pv == null || GetActionsStatesMethod == null) return null;

            var actionsState = GetActionsStatesMethod.Invoke(turn, new object[] { cu }) as ActionsState;
            if (actionsState == null) return null;
            actionsState.ApproachPoint = point;
            actionsState.ApproachRadius = approachRadius; // 0.3 ~= exact (move); the attack reach for an approach
            actionsState.NeedLOS = false;
            pv.CalculatePathForCommand(cu, actionsState, updateActionsState: true);

            var path = pv.CurrentPathForUnit(cu.View);
            if (path == null || path.vectorPath == null || path.vectorPath.Count == 0) return null;
            return path.vectorPath;
        }

        /// <summary>
        /// The unit cursor-relative things (recenter, distance/bearing readouts, scan origin) should anchor
        /// to. In turn-based that's the unit whose turn it is — so "c" recenters on the acting unit and you
        /// navigate/attack/move relative to it, not the main character. Null outside turn-based (callers fall
        /// back to the main character).
        /// </summary>
        public static UnitEntityData ReferenceUnit => InTurnBased ? CurrentUnit : null;

        // Announce whose turn it is when the active unit changes — the cue to act (your unit) or wait (an
        // enemy) — and when a player unit's turn ENDS. The end cue watches the game's own end-turn signal:
        // every end path (Space/ForceToEnd, auto-end on exhausted actions, AI) goes through
        // TurnController.End() → Status = Ended, so we track the live turn object and fire on that
        // transition (the ITurnBasedUnitTurnEndedHandler EventBus interface is never raised by the game).
        // Enemy turn-ends stay silent — the next "X's turn" cue already covers them. Ticked from Main.OnUpdate.
        private static UnitEntityData _lastTurn;
        private static TurnController _trackedTurn;
        private static UnitEntityData _trackedUnit;
        public static void TickTurn()
        {
            if (!InTurnBased) { _lastTurn = null; _trackedTurn = null; _trackedUnit = null; return; }

            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            // The tracked turn finished (reached Ended, or was disposed/replaced between frames).
            if (_trackedTurn != null && (turn != _trackedTurn || _trackedTurn.Status == TurnController.TurnStatus.Ended))
            {
                if (_trackedUnit != null && _trackedUnit.IsDirectlyControllable)
                    Tts.Speak(Message.Localized("ui", "combat.turn_ended", new { name = _trackedUnit.CharacterName }).Resolve());
                _trackedTurn = null;
                _trackedUnit = null;
            }
            if (turn != null && _trackedTurn == null && turn.Status != TurnController.TurnStatus.Ended)
            {
                _trackedTurn = turn;
                _trackedUnit = turn.Rider; // the turn's owner (stable), not the selection-dependent SelectedUnit
            }

            var cur = CurrentUnit;
            if (cur == _lastTurn) return;
            _lastTurn = cur;
            if (cur != null)
                Tts.Speak(Message.Localized("ui", cur.IsPlayersEnemy ? "combat.turn_enemy" : "combat.turn",
                    new { name = cur.CharacterName }).Resolve());
        }

        /// <summary>
        /// R: the acting unit's action economy + remaining movement. Availability comes from the game's own
        /// checks (the same Has*Action calls its turn logic uses); movement is the remaining range with both
        /// actions spent on moving — the same budget the path verdict compares against. Gated to focus-mode
        /// exploration like the scanner keys.
        /// </summary>
        public static void AnnounceStatus()
        {
            if (!FocusMode.Active) return;
            var screen = WrathAccess.Screens.ScreenManager.Current;
            if (screen == null || screen.Key != "ctx.ingame") return;
            if (!InTurnBased) { Tts.Speak(Message.Localized("ui", "combat.not_turn_based").Resolve(), interrupt: true); return; }
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            if (turn == null || cu == null) { Tts.Speak(Message.Localized("ui", "combat.no_active_turn").Resolve(), interrupt: true); return; }

            string State(bool available) => Message.Localized("ui", available ? "combat.available" : "combat.used").Resolve();
            var sb = new System.Text.StringBuilder(Message.Localized("ui", "combat.status_actions", new
            {
                name = cu.CharacterName,
                standard = State(cu.HasStandardAction()),
                move = State(cu.HasMoveAction()),
                swift = State(cu.HasSwiftAction()),
            }).Resolve());
            // Two numbers, like the game's path break markers: the move action alone, and (when different)
            // the maximum with the standard action converted to a second move.
            int moveFt = Mathf.RoundToInt(
                cu.CombatState.TBM.GetRemainingMovementRange(total: false, singleActionMove: false) / Geo.MetresPerFoot);
            int totalFt = Mathf.RoundToInt(
                cu.CombatState.TBM.GetRemainingMovementRange(total: true, singleActionMove: false) / Geo.MetresPerFoot);
            sb.Append(", ").Append(Message.Localized("ui", "combat.movement_remaining", new { feet = moveFt }).Resolve());
            if (totalFt > moveFt) sb.Append(", ").Append(Message.Localized("ui", "combat.with_standard", new { feet = totalFt }).Resolve());
            if (turn.HasFiveFootStep(cu)) sb.Append(", ").Append(Message.Localized("ui", "combat.five_foot_step").Resolve());
            Tts.Speak(sb.ToString(), interrupt: true);
        }
    }
}
