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

        // Sonar cue by faction — the same split the scanner uses (Party / Enemies / Neutrals): your party are
        // allies, hostiles are enemies, everyone else neutral.
        public override string SonarSound =>
            _unit.IsPlayerFaction ? "units-ally" : _unit.IsPlayersEnemy ? "units-enemy" : "units-neutral";

        // The unit's body radius (size-scaled) — what combat reach uses for edge-to-edge distance, so a
        // Large/Huge creature correctly reports a footprint spanning several tiles.
        public override float Footprint => (_unit.View as Kingmaker.View.UnitEntityView)?.Corpulence ?? 0f;

        public override IEnumerable<ScanCategory> Categories
        {
            get
            {
                if (_unit.IsPlayerFaction) yield return ScanCategory.Party;
                else if (_unit.IsPlayersEnemy) yield return ScanCategory.Enemies;
                else yield return ScanCategory.Neutrals;
                if (_unit.IsDeadAndHasLoot) yield return ScanCategory.Containers;
            }
        }

        protected override string Extra
        {
            get
            {
                var state = _unit.State;
                if (state.IsDead) return "dead";
                if (!state.IsConscious) return "unconscious";
                var hp = "HP " + _unit.HPLeft + " of " + _unit.MaxHP;
                if (_unit.IsInCombat) hp += ", in combat";
                return hp;
            }
        }

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
