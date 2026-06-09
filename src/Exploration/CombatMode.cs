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
        public static Vector3? PathEndpointToward(Vector3 point)
        {
            if (!InTurnBased) return null;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            var pv = PathVisualizer.Instance;
            if (turn == null || cu == null || cu.View == null || pv == null || GetActionsStatesMethod == null) return null;

            var actionsState = GetActionsStatesMethod.Invoke(turn, new object[] { cu }) as ActionsState;
            if (actionsState == null) return null;
            actionsState.ApproachPoint = point;
            actionsState.ApproachRadius = 0.3f; // small tolerance, like UnitMoveTo
            actionsState.NeedLOS = false;
            pv.CalculatePathForCommand(cu, actionsState, updateActionsState: true);

            var path = pv.CurrentPathForUnit(cu.View);
            if (path == null || path.vectorPath == null || path.vectorPath.Count == 0) return null;
            return path.vectorPath[path.vectorPath.Count - 1];
        }

        /// <summary>
        /// The unit cursor-relative things (recenter, distance/bearing readouts, scan origin) should anchor
        /// to. In turn-based that's the unit whose turn it is — so "c" recenters on the acting unit and you
        /// navigate/attack/move relative to it, not the main character. Null outside turn-based (callers fall
        /// back to the main character).
        /// </summary>
        public static UnitEntityData ReferenceUnit => InTurnBased ? CurrentUnit : null;

        // Announce whose turn it is when the active unit changes — the cue to act (your unit) or wait (an
        // enemy). Ticked from Main.OnUpdate.
        private static UnitEntityData _lastTurn;
        public static void TickTurn()
        {
            if (!InTurnBased) { _lastTurn = null; return; }
            var cur = CurrentUnit;
            if (cur == _lastTurn) return;
            _lastTurn = cur;
            if (cur != null)
                Tts.Speak(cur.CharacterName + (cur.IsPlayersEnemy ? "'s turn, enemy" : "'s turn"));
        }
    }
}
