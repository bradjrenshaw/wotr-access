using System.Collections.Generic;
using Kingmaker.Controllers.Clicks.Handlers; // ClickUnitHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UnitLogic.Commands; // UnitAttack (approach radius)

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

        // name, type (faction), then either a terminal condition (dead/unconscious) OR hp (+ in-combat).
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            foreach (var p in NameAndType(_unit.CharacterName, FactionWord())) yield return p;

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

        // Same as clicking the unit: the handler selects/talks/attacks/loots as appropriate and derives
        // the acting unit itself (nearest selected + main-player-preferred).
        public override bool Interact()
        {
            var view = _unit.View;
            if (view == null) return false;
            // Turn-based: pre-compute the approach path to the attack's reach (so the command has a route to
            // walk — without it the game's mouse-driven prediction never ran and the unit wouldn't move).
            // But REFUSE the attack if reaching attack range would take more than the MOVE action: the game
            // would otherwise spend the standard action as a second move and never strike — a far enemy that
            // needs move+standard just to reach gets run into for nothing. The player can still close
            // deliberately with move-to-cursor (Backspace), which is free to spend both actions on movement.
            if (CombatMode.InTurnBased)
            {
                var attacker = CombatMode.CurrentUnit;
                if (attacker != null && attacker.View != null && attacker.CanAttack(_unit))
                {
                    float reach = UnitAttack.GetApproachRadius(attacker.GetFirstWeapon(), attacker, _unit);
                    if (CombatMode.TryApproach(_unit.Position, reach, out float walk, out float moveRange)
                        && walk > moveRange)
                    {
                        Tts.Speak(Loc.T("combat.too_far_to_attack"));
                        return true; // handled (no command issued); the unit stays put rather than burning both actions
                    }
                }
            }
            return new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
        }
    }
}
