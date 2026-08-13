using System.Collections.Generic;
using Kingmaker.Blueprints; // GetComponent extension for blueprints (ILineOfSightIgnore probe)
using Kingmaker.Controllers.Clicks.Handlers; // ClickUnitHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UnitLogic.Commands; // UnitAttack (approach radius)
using Kingmaker.UnitLogic.Commands.Base; // UnitCommand (IsUnitCloseEnough)

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A unit (party member, NPC, enemy). Faction decides its primary category; a lootable corpse is
    /// also a Container (the many-to-many case). State line: dead/unconscious, else HP (+ in combat).
    /// </summary>
    internal sealed class ProxyUnit : ProxyEntity
    {
        private readonly UnitEntityData _unit;

        public ProxyUnit(UnitEntityData unit) : base(unit) { _unit = unit; }

        public override string Name => _unit.CharacterName;

        public override bool IsUnit => true;

        public override UnitEntityData TargetUnit => _unit; // ability targeting picks this unit

        // Primary node: faction while alive (the scanner's Party/Enemies/Neutrals split); a dead unit
        // with loot is primarily a corpse-container (its relevance is the loot — the sonar follows the
        // user's corpse sound, while Categories keeps it listed under its faction too); a dead empty
        // unit drops out of the taxonomy (silent, no longer relevant).
        public override string Primary
        {
            get
            {
                if (_unit.State.IsDead)
                    return _unit.IsDeadAndHasLoot ? ScanTaxonomy.ContainersCorpse : null;
                return _unit.IsPlayerFaction ? ScanTaxonomy.UnitsParty
                    : _unit.IsPlayersEnemy ? ScanTaxonomy.UnitsEnemies : ScanTaxonomy.UnitsNeutrals;
            }
        }

        // The unit's body radius (size-scaled) — what combat reach uses for edge-to-edge distance, so a
        // Large/Huge creature correctly reports a footprint spanning several tiles.
        public override float Footprint => (_unit.View as Kingmaker.View.UnitEntityView)?.Corpulence ?? 0f;

        public override IEnumerable<string> Nodes
        {
            get
            {
                yield return _unit.IsPlayerFaction ? "units.party"
                    : _unit.IsPlayersEnemy ? "units.enemies" : "units.neutrals";
                // A lootable corpse is also a container (its faction stays its identity for browsing).
                if (_unit.IsDeadAndHasLoot) yield return "containers.corpse";
            }
        }

        // Announce as the faction (stable) even when dead/looted — so a corpse reads "<name>, enemy,
        // dead", not as a container. (The sound Primary still flips to containers.corpse.)
        protected override string AnnounceNode => _unit.IsPlayerFaction ? ScanTaxonomy.UnitsParty
            : _unit.IsPlayersEnemy ? ScanTaxonomy.UnitsEnemies : ScanTaxonomy.UnitsNeutrals;

        // name, interaction (talk/interactive — how the unit answers the interact keys), type (faction),
        // current action (casting/attacking/moving), then either a terminal condition (dead/unconscious)
        // OR hp (+ in-combat). The action part self-skips when idle.
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            bool hasName = !string.IsNullOrEmpty(_unit.CharacterName);
            // Mirrors NameAndType's rule: with no real name the faction word plays the name role.
            yield return new Announce.NamePart(hasName ? _unit.CharacterName : FactionWord());
            var interact = InteractionKey();
            if (interact != null) yield return new Announce.InteractionPart(interact);
            if (hasName) yield return new Announce.TypePart(FactionWord());
            yield return new Announce.ActionPart(CombatMode.DescribeAction(_unit));

            var state = _unit.State;
            if (state.IsDead) yield return new Announce.ConditionPart("unit.dead");
            else if (!state.IsConscious) yield return new Announce.ConditionPart("unit.unconscious");
            else
            {
                yield return new Announce.HpPart(_unit.HPLeft, _unit.MaxHP);
                if (_unit.IsInCombat) yield return new Announce.ConditionPart("unit.in_combat");
            }
        }

        private string FactionWord()
            => _unit.IsPlayerFaction ? Loc.T("scan.faction.party")
             : _unit.IsPlayersEnemy ? Loc.T("scan.faction.enemy")
             : Loc.T("scan.faction.neutral");

        // The ui-table key for how this unit answers the interact keys right now, or null when it
        // wouldn't respond. Asks the game the exact question its click handler asks —
        // UnitPartInteractions.SelectClickInteraction for the unit that would initiate — so the readout
        // can never promise more than a click delivers (conditions, cutscene disables, per-initiator
        // rules all apply). Combat suppresses it: the click path refuses interactions mid-fight.
        private string InteractionKey()
        {
            if (_unit.State.IsDead || _unit.IsInCombat || _unit.IsPlayerFaction) return null;
            var part = _unit.Get<Kingmaker.UnitLogic.Parts.UnitPartInteractions>();
            if (part == null) return null;
            var initiator = Kingmaker.Game.Instance?.UI?.SelectionManager?.GetNearestSelectedUnit(_unit.Position)
                ?? Kingmaker.Game.Instance?.Player?.MainCharacter.Value;
            if (initiator == null || initiator.IsInCombat) return null;
            var inter = part.SelectClickInteraction(initiator);
            if (inter == null) return null;
            // Dialogue and barks are both "talk" to the player (every spoken response); scripted
            // action/cutscene interactions read "interactive".
            var src = inter is Kingmaker.UnitLogic.Interaction.SpawnerInteractionPart.Wrapper w
                ? (object)w.Source
                : inter is Kingmaker.AreaLogic.Etudes.EtudeBracketOverrideUnitInteraction e ? e.Source : inter;
            var n = src.GetType().Name;
            return n.Contains("Dialog") || n.Contains("Bark") || n.Contains("Companion")
                ? "scan.interact.talk" : "scan.interact.interactive";
        }

        // Same as clicking the unit: the handler selects/talks/attacks/loots as appropriate and derives
        // the acting unit itself (nearest selected + main-player-preferred).
        public override InteractOutcome Interact()
        {
            var view = _unit.View;
            if (view == null) return InteractOutcome.NotSupported;

            // A held touch charge turns the interact keys into the DELIVER keys for valid targets —
            // the sighted equivalent is the plain deliver-cursor click (the deliver ability is
            // deliberately hidden from the action bar, so there's no slot to press). Invalid targets
            // and ground actions keep their normal behaviour. Turn-based gates the walk like attacks.
            var touch = TouchCharge.Held(out var holder);
            if (touch != null && TouchCharge.CanDeliverTo(touch, _unit))
            {
                if (CombatMode.InTurnBased && holder == CombatMode.CurrentUnit && holder != _unit)
                {
                    float treach = touch.Ability.Data.GetApproachDistance(_unit);
                    // Same LOS rule the deliver command applies (UnitUseAbility: LOS unless the ability
                    // opts out) — reach without sight isn't reach, and the path must end where the
                    // command can actually act.
                    bool tlos = touch.Ability.Blueprint
                        .GetComponent<Kingmaker.UnitLogic.Abilities.Components.TargetCheckers.ILineOfSightIgnore>() == null;
                    bool inReach = UnitCommand.IsUnitCloseEnough(_unit.Position, holder.Position,
                        holder.EyePosition, treach, needLOS: tlos, ignoreBlockerRadius: 0f);
                    // A touch delivered the round it was CAST is a free action (UnitCommand.IsFreeTouch →
                    // IsIgnoreCooldown), so the walk needn't leave a standard action alive; a charge held
                    // from an earlier round costs the standard again and gets the full economy gate.
                    bool freeTouch = holder.Get<Kingmaker.UnitLogic.Parts.UnitPartTouch>()?.IsCastedInThisRound ?? false;
                    if (!inReach && !freeTouch && CombatMode.MoveThenActBlocked(holder))
                    {
                        Tts.Speak(Loc.T("combat.cant_move_and_act"), interrupt: true);
                        return InteractOutcome.RefusedSpoken; // no path computed, nothing to cancel
                    }
                    if (!inReach
                        && !(CombatMode.TryApproach(_unit.Position, treach, out float twalk, out float tmove,
                                needLOS: tlos, requireStandardAfter: !freeTouch)
                             && twalk <= tmove))
                    {
                        CombatMode.CancelPathReservation(); // no command follows the computed path
                        Tts.Speak(Loc.T("combat.too_far_to_cast"), interrupt: true);
                        return InteractOutcome.RefusedSpoken;
                    }
                    // In reach: no path was computed, so drop any stale reservations before issuing.
                    if (inReach) CombatMode.CancelPathReservation();
                }
                TouchCharge.Deliver(holder, touch, _unit);
                CombatMode.NoteIssuedCommand(holder);
                Tts.Speak(Loc.T("touch.delivering",
                    new { spell = touch.Ability.Data.Name, name = _unit.CharacterName }), interrupt: true);
                return InteractOutcome.RefusedSpoken; // we spoke the outcome ourselves
            }
            // Turn-based: pre-compute the approach path to the attack's reach (so the command has a route to
            // walk — without it the game's mouse-driven prediction never ran and the unit wouldn't move).
            // But REFUSE the attack when attack range can't be reached within the MOVE action — whether the
            // walk is too long OR there's no route at all: the game would otherwise spend the standard
            // action as a second move and never strike. RefusedSpoken keeps the caller from talking over
            // the refusal (it used to announce "Interacting with …" on top). The player can still close
            // deliberately with move-to-cursor (Backspace), which is free to spend both actions on movement.
            if (CombatMode.InTurnBased)
            {
                var attacker = CombatMode.CurrentUnit;
                if (attacker != null && attacker.View != null && attacker.CanAttack(_unit))
                {
                    // Gate on the command the click factory will ACTUALLY issue: when an auto-use
                    // ability (the action-bar "default attack", e.g. a cantrip) is set and available,
                    // ClickUnitHandler.CreateCommand casts IT instead of UnitAttack — so reach and the
                    // LOS rule come from the ABILITY, not the weapon (a melee-armed caster 25ft out
                    // casts from where they stand; the weapon gate wrongly refused or walked them in).
                    // Personal-range auto-use casts on the caster in place: nothing to approach.
                    var auto = attacker.Brain.GetAvailableAutoUseAbility();
                    bool inPlace = auto != null
                        && auto.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal;
                    if (!inPlace)
                    {
                        float reach; bool needLos; string tooFarKey;
                        if (auto != null)
                        {
                            reach = auto.GetApproachDistance(_unit);
                            // Same LOS rule the deliver command applies (UnitUseAbility: LOS unless
                            // the ability opts out) — matches the touch-charge branch above.
                            needLos = auto.Blueprint.GetComponent<
                                Kingmaker.UnitLogic.Abilities.Components.TargetCheckers.ILineOfSightIgnore>() == null;
                            tooFarKey = "combat.too_far_to_cast";
                        }
                        else
                        {
                            reach = UnitAttack.GetApproachRadius(attacker.GetFirstWeapon(), attacker, _unit);
                            // UnitAttack ALWAYS requires line of sight (its ctor sets NeedLoS) — check
                            // reach and compute the approach the same way, so a ranged target behind a
                            // wall either gets a path to a firing position or an honest refusal (a
                            // sightless path made the command give up on the spot and auto-end the
                            // turn — the Camellia/centipede repro).
                            needLos = true;
                            tooFarKey = "combat.too_far_to_attack";
                        }
                        bool inReach = UnitCommand.IsUnitCloseEnough(_unit.Position, attacker.Position,
                            attacker.EyePosition, reach, needLOS: needLos, ignoreBlockerRadius: 0f);
                        // Out of reach + no time for both the walk and the swing (surprise round,
                        // staggered, standard already spent): refuse with the action-economy reason.
                        if (!inReach && CombatMode.MoveThenActBlocked(attacker))
                        {
                            Tts.Speak(Loc.T("combat.cant_move_and_act"), interrupt: true);
                            return InteractOutcome.RefusedSpoken; // no path computed, nothing to cancel
                        }
                        if (!inReach
                            && !(CombatMode.TryApproach(_unit.Position, reach, out float walk, out float moveRange, needLOS: needLos)
                                 && walk <= moveRange))
                        {
                            CombatMode.CancelPathReservation(); // no command follows the computed path
                            Tts.Speak(Loc.T(tooFarKey), interrupt: true);
                            return InteractOutcome.RefusedSpoken; // no command issued; the unit stays put
                        }
                        // In reach: no path was computed, so drop any stale reservations before issuing.
                        if (inReach) CombatMode.CancelPathReservation();
                    }
                }
            }
            bool started = new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
            if (started) CombatMode.NoteIssuedCommand(CombatMode.CurrentUnit);
            return started ? InteractOutcome.Started : InteractOutcome.NotSupported;
        }
    }
}
