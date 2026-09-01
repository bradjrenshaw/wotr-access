using HarmonyLib;
using TurnBased.Controllers;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Silences the game's mouse-hover prediction loop (<c>TurnController.UpdateActionPredictions</c>)
    /// during player turns while the mod is enabled. The loop re-runs after EVERY command ends
    /// (<c>HandleUnitCommandDidEnd</c> sets <c>m_NeedNewPredictions</c>) and derives a phantom command
    /// from the PHYSICAL mouse: a simulated click on the hovered unit/object, or — over bare terrain —
    /// a path to the point under the OS cursor, written into the live turn state with
    /// <c>updateActionsState: true</c>. For a mouse player those predictions are continuously
    /// overwritten and the eventual click matches the last one; for a keyboard player the mouse is
    /// parked somewhere arbitrary, so the junk reservation sits until the next command's start/end
    /// conversion (<c>CombatAction.UpdateCurrentStates</c>) turns it into REAL activity losses — the
    /// long-standing symptom was every spell greying out after any movement. With the loop dead, the
    /// mod is the sole author of predictions: movement via <c>CombatMode.ComputePath</c> (commit mode),
    /// actions via <c>CombatMode.NoteIssuedCommand</c> after each command we issue.
    /// </summary>
    /// <summary>
    /// Companion to <see cref="TurnPredictionPatch"/>: with the prediction loop dead, the smart
    /// cursor's <c>m_AttackMode</c> is never updated and stays at its default — <c>SingleAttack</c>
    /// (enum 0). <c>GetEnabledFullAttack</c> reads that mode for the un-moved current unit, so every
    /// keyboard-flow attack silently became a single attack: Rapid Shot, Haste and iterative attacks
    /// never fired. The mode is purely the mouse UI's downgrade dial; the RULES restrictions live
    /// elsewhere and still apply (<c>UsedOneMoveAction</c> → single, staggered → single, …).
    ///
    /// Answer with VANILLA'S OWN RULE for when the smart cursor offers a full attack
    /// (<c>TurnController.UpdateSmartCursorVariants</c>): the unit must have MORE THAN ONE attack
    /// (<c>UnitAttack.EstimateFullAttacks</c>) and still hold a full-round action (or a prepared
    /// spell combat). A blanket "true" made single-attack characters full-attack too, and a
    /// one-attack "full attack" is a split-brain command: the prediction layer (ours and the game's
    /// both key on <c>IsFullAttack()</c>) marks the MOVE slot used, so every move-action ability
    /// greys out for the rest of the turn, while <c>SpendAction</c> (which keys on
    /// <c>IsFullRoundAction()</c>, false for a single attack) never charges the move cooldown — the
    /// tester's "attacked, then couldn't use a move action, yet R said move available".
    /// </summary>
    [HarmonyPatch(typeof(TurnController), "GetEnabledFullAttack")]
    internal static class FullAttackModePatch
    {
        private static bool Prefix(Kingmaker.EntitySystem.Entities.UnitEntityData unit, ref bool __result)
        {
            if (!Main.Enabled || unit == null) return true;
            __result = Kingmaker.UnitLogic.Commands.UnitAttack.EstimateFullAttacks(unit) > 1
                && (unit.HasFullRoundAction() || unit.PreparedSpellCombat());
            return false;
        }
    }

    [HarmonyPatch(typeof(TurnController), "UpdateActionPredictions")]
    internal static class TurnPredictionPatch
    {
        private static readonly System.Reflection.FieldInfo NeedNewPredictions =
            AccessTools.Field(typeof(TurnController), "m_NeedNewPredictions");

        private static bool Prefix(TurnController __instance)
        {
            if (!Main.Enabled) return true;
            var unit = __instance.SelectedUnit;
            if (unit == null || !unit.IsDirectlyControllable) return true; // AI turns keep vanilla flow
            NeedNewPredictions.SetValue(__instance, false); // consume the request so the loop never spins
            return false;
        }
    }
}
