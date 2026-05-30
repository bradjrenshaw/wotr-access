using System.Collections.Generic;
using Kingmaker.Controllers.Clicks.Handlers; // ClickUnitHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData

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
            return new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
        }
    }
}
