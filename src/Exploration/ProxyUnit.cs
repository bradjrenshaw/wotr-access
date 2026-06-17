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
                    return _unit.IsDeadAndHasLoot ? SonarTaxonomy.ContainersCorpse : null;
                return _unit.IsPlayerFaction ? SonarTaxonomy.Party
                    : _unit.IsPlayersEnemy ? SonarTaxonomy.Enemies : SonarTaxonomy.Neutrals;
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

        protected override string AnnounceKey => "unit";

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
            // Turn-based: if this is an attackable enemy, pre-compute the approach path using the attack's
            // reach as the radius — so the path lands on a reachable melee space (not the enemy's own tile)
            // and the attack's approach has a route to walk. Without it the approach has no path (the game's
            // mouse-driven prediction never produced one) and the unit doesn't move. UnitAttack itself walks
            // as far as the budget allows and strikes only if it reaches range.
            if (CombatMode.InTurnBased)
            {
                var attacker = CombatMode.CurrentUnit;
                if (attacker != null && attacker.View != null && attacker.CanAttack(_unit))
                {
                    float reach = UnitAttack.GetApproachRadius(attacker.GetFirstWeapon(), attacker, _unit);
                    CombatMode.PathEndpointToward(_unit.Position, reach); // populates the approach path
                }
            }
            return new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
        }
    }
}
