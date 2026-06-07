using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickWithSelectedAbilityHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.MVVM._VM.ActionBar; // ActionBarSlotVM
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot subtypes
using Kingmaker.UnitLogic.Abilities; // AbilityData
using Kingmaker.UnitLogic.Abilities.Blueprints; // AbilityTargetAnchor
using Kingmaker.UnitLogic.Commands; // UnitUseAbility
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Accessible ability targeting — reuses the game's own aim flow rather than a bespoke screen. Activating
    /// a targeted ability puts the game into <c>PointerMode.Ability</c> via
    /// <see cref="ClickWithSelectedAbilityHandler.SetAbility"/>; the live world cursor then lets the player
    /// look around the battlefield, and the existing act-on-target inputs commit the cast while aiming:
    /// <b>Enter</b> casts at the cursor (unit under it, else the point), <b>I</b> casts at the selected
    /// scanner item, <b>Backspace</b>/<b>Escape</b> cancel. The commit goes through the handler's
    /// <c>OnClick</c>, so all of the game's validation, target restrictions, refusal messaging, and the cast
    /// command are reused.
    ///
    /// Activation branches by ability kind: self/no-target casts immediately; a targeted spell/ability
    /// enters aim; a targeted activatable (e.g. Saddle Up) flips on and aims its <c>SelectTargetAbility</c>
    /// (which is what stops the activatable controller from reverting it); a plain toggle just flips.
    /// </summary>
    internal static class Targeting
    {
        private static ClickWithSelectedAbilityHandler Handler => Game.Instance?.SelectedAbilityHandler;

        /// <summary>True while an ability is aimed and waiting for a target (any caster — ours or the game's).</summary>
        public static bool Aiming => Handler != null && Handler.SelectedAbility != null;

        // ---- activation (from the action bar) ----

        public static void Activate(ActionBarSlotVM vm)
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
            if (announceName != null) Tts.Speak("Targeting " + announceName + ", choose a target", interrupt: true);
        }

        private static void CastOnSelf(AbilityData ability)
        {
            var caster = ability?.Caster?.Unit;
            if (caster == null) return;
            caster.Commands.Run(UnitUseAbility.CreateCastCommand(ability, caster)); // game's factory (matches OnClick)
        }

        // ---- target selection (while aiming) ----

        public static void Cancel()
        {
            if (!Aiming) return;
            Game.Instance?.ClickEventsController?.ClearPointerMode(); // → DropAbility()
            Tts.Speak("Targeting cancelled", interrupt: true);
        }

        /// <summary>Enter: cast at the world cursor — the unit under it if any, else the cursor point.</summary>
        public static void CommitAtCursor()
        {
            if (!Aiming) return;
            if (!Cursor.Has) { Tts.Speak("No cursor set", interrupt: true); return; }
            Commit(CursorTarget.Inside()?.TargetUnit, Cursor.Position.Value);
        }

        /// <summary>I: cast at the selected scanner item — its unit if entity-backed, else its point.</summary>
        public static void CommitOn(ScanItem item)
        {
            if (!Aiming) return;
            if (item == null) { Tts.Speak("No item selected", interrupt: true); return; }
            Commit(item.TargetUnit, item.Position);
        }

        private static void Commit(UnitEntityData unit, Vector3 point)
        {
            var ability = Handler != null ? Handler.SelectedAbility : null;
            if (ability == null) return;
            var go = unit != null && unit.View != null ? unit.View.gameObject : null;
            // Let the game validate + cast (OnClick: GetTarget, restrictions incl. mount delegation, the cast
            // command, pointer-mode clear). On refusal it raises IWarningNotificationUIHandler with the exact
            // reason, which WarningReader speaks — so we don't second-guess it. OnClick returns true only if
            // it actually issued the cast.
            if (Handler.OnClick(go, point, 0))
                Tts.Speak(unit != null ? "Casting on " + unit.CharacterName : "Casting", interrupt: true);
        }
    }
}
