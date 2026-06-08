using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Metamagic; // SpellbookMetamagicSlotVM, SpellbookSpellLevelSelectorVM
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One metamagic feat in the metamagic builder (<see cref="SpellbookMetamagicSlotVM"/>) — a toggle that
    /// applies/removes that metamagic on the spell being built. Reads name + level cost as a toggle with its
    /// on/off state; Enter toggles it (the game's OnSelect → add/remove on the MetamagicBuilder). Carries the
    /// feat's tooltip.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement))]
    public sealed class ProxyMetamagicToggle : UIElement
    {
        private readonly SpellbookMetamagicSlotVM _vm;

        public ProxyMetamagicToggle(SpellbookMetamagicSlotVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private string Label()
        {
            var name = _vm.Feature?.Name ?? "";
            return name + " (+" + _vm.Cost + ")"; // +N spell levels
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Label()));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(Message.Localized("ui", _vm.IsSelected ? "value.on" : "value.off"));
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _vm.Tooltip;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"), _ => _vm.OnSelect());
        }
    }

    /// <summary>
    /// The Heighten level stepper (<see cref="SpellbookSpellLevelSelectorVM"/>) — only present when Heighten
    /// Spell is applied. Reads the result spell level; Increase / Decrease raise/lower the heightened level
    /// within the caster's range.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement))]
    public sealed class ProxyMetamagicLevel : UIElement
    {
        private readonly SpellbookSpellLevelSelectorVM _vm;

        public ProxyMetamagicLevel(SpellbookSpellLevelSelectorVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "metamagic.spell_level"));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Raw(_vm.ResultSpellLevel.Value.ToString()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_vm.CanIncrease)
                yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.increase"), _ => _vm.IncreaseSpellLevel());
            if (_vm.CanDecrease)
                yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.decrease"), _ => _vm.DecreaseSpellLevel());
        }
    }
}
