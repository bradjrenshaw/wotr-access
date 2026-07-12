using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickWithSelectedAbilityHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.MVVM._VM.ActionBar; // ActionBarSlotVM
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot subtypes
using Kingmaker.UnitLogic.Abilities; // AbilityData
using Kingmaker.UnitLogic.Abilities.Blueprints; // AbilityTargetAnchor
using Kingmaker.UnitLogic.Commands; // UnitUseAbility
using Kingmaker.UnitLogic.Commands.Base; // UnitCommand (IsUnitCloseEnough)
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Targeting for action-bar abilities/spells. Activation puts the game into <c>PointerMode.Ability</c>
    /// via <see cref="ClickWithSelectedAbilityHandler.SetAbility"/>; committing routes through the handler's
    /// <c>OnClick</c>, so all of the game's validation, target restrictions, refusal messaging and the cast
    /// command are reused. <see cref="Active"/> is read live from the handler, so cancelling elsewhere
    /// (right-click, a mode switch) clears us too.
    ///
    /// Activation branches by ability kind: self/no-target casts immediately; a targeted spell/ability enters
    /// aim; a targeted activatable (e.g. Saddle Up) flips on and aims its <c>SelectTargetAbility</c> (which is
    /// what stops the activatable controller from reverting it); a plain toggle just flips.
    /// </summary>
    internal sealed class AbilityTargeting : ITargetingMode
    {
        private static ClickWithSelectedAbilityHandler Handler => Game.Instance?.SelectedAbilityHandler;

        public bool Active => Handler != null && Handler.SelectedAbility != null;

        public void Activate(ActionBarSlotVM vm)
        {
            var slot = vm?.MechanicActionBarSlot;
            if (slot == null) return;

            // Activatable toggle: just flip it. The game's ActivatableAbility.SetIsOn ENTERS AIM ITSELF for a
            // targeted toggle (Saddle Up) — it calls SelectedAbilityHandler.SetAbility(SelectTargetAbility.Data)
            // when IsWaitingForTarget. So we must NOT call SetAbility too: a second SetAbility with the same
            // ability toggles aim straight back off (that was the "targeting → off" bug). A plain toggle just
            // flips; the slot's on/off watcher announces the result (incl. "targeting").
            if (slot is MechanicActionBarSlotActivableAbility)
            {
                vm.OnMainClick();
                return;
            }

            var ability = AbilityOf(slot);
            if (ability == null) { vm.OnMainClick(); return; } // items/unknown kinds: fall back for now

            // Mirror MechanicActionBarSlotAbility.OnClick: a regular ability only aims/casts when it's actually
            // castable now. Otherwise route through OnMainClick — which plays the can't-use sound and raises
            // the game's reason (spoken by WarningReader) — instead of aiming/issuing a command the game won't
            // run (a dead, piling-up queue, e.g. Charge with no valid charge).
            if (!slot.IsPossibleActive()) { vm.OnMainClick(); return; }

            if (ability.TargetAnchor == AbilityTargetAnchor.Owner) { CastOnSelf(ability); return; } // self/no target
            Begin(ability, slot.GetTitle()); // targeted: enter aim; player picks via cursor (Enter) / scanner (I)
        }

        private static AbilityData AbilityOf(MechanicActionBarSlot slot)
        {
            if (slot is MechanicActionBarSlotAbility a) return a.Ability;
            if (slot is MechanicActionBarSlotSpell s) return s.Spell;
            return null;
        }

        private static void Begin(AbilityData ability, string announceName)
        {
            Handler?.SetAbility(ability);
            if (announceName != null) Tts.Speak(Loc.T("target.begin", new { name = announceName }), interrupt: true);
        }

        private static void CastOnSelf(AbilityData ability)
        {
            var caster = ability?.Caster?.Unit;
            if (caster == null) return;
            caster.Commands.Run(UnitUseAbility.CreateCastCommand(ability, caster)); // game's factory (matches OnClick)
        }

        public void CommitAt(UnitEntityData unit, Vector3 point)
        {
            var ability = Handler != null ? Handler.SelectedAbility : null;
            if (ability == null) return;
            var go = unit != null && unit.View != null ? unit.View.gameObject : null;

            // Turn-based TOUCH cast on an out-of-reach target: vanilla casts IN PLACE onto a held
            // charge (see TouchApproachPatch) — silent and baffling by ear. Our behavior instead:
            // approach-then-cast when the MOVE action's range covers the walk (mirrors the attack rule
            // in ProxyUnit — the standard action must survive for the cast itself), and REFUSE ALOUD
            // when it can't. Never the silent held-charge trap.
            bool wantApproach = false;
            var caster = ability.Caster != null ? ability.Caster.Unit : null;
            if (CombatMode.InTurnBased && unit != null && caster != null && unit != caster
                && caster == CombatMode.CurrentUnit && caster.IsDirectlyControllable
                && ability.Blueprint.StickyTouch != null)
            {
                float reach = ability.GetApproachDistance(unit);
                // The command system's own "am I in range" test (same statics UnitCommand.Start uses).
                bool inReach = UnitCommand.IsUnitCloseEnough(unit.Position, caster.Position,
                    caster.EyePosition, reach, needLOS: false, ignoreBlockerRadius: 0f);
                if (!inReach)
                {
                    // TryApproach also POPULATES the path, so the issued command has a route to walk.
                    if (CombatMode.TryApproach(unit.Position, reach, out float walk, out float moveRange)
                        && walk <= moveRange)
                        wantApproach = true;
                    else
                    {
                        Tts.Speak(Loc.T("combat.too_far_to_cast"), interrupt: true);
                        return;
                    }
                }
            }

            // Let the game validate + cast (OnClick: GetTarget, restrictions incl. mount delegation, the cast
            // command, pointer-mode clear). On refusal it raises IWarningNotificationUIHandler with the exact
            // reason, which WarningReader speaks — so we don't second-guess it. OnClick returns true only if
            // it actually issued the cast.
            if (Handler.OnClick(go, point, 0))
            {
                if (wantApproach)
                {
                    var cmd = caster.Commands.GetCommand<UnitUseAbility>();
                    if (cmd != null && cmd.Ability != null && cmd.Ability.Blueprint == ability.Blueprint)
                        WrathAccess.Patches.TouchApproachPatch.Enable(cmd);
                }
                Tts.Speak(unit != null ? Loc.T("target.cast_on", new { name = unit.CharacterName }) : Loc.T("target.cast"), interrupt: true);
            }
        }

        public void Cancel()
        {
            Game.Instance?.ClickEventsController?.ClearPointerMode(); // → DropAbility()
            Tts.Speak(Loc.T("target.cancelled"), interrupt: true);
        }

        public void Tick() { } // abilities need no per-frame upkeep
    }
}
