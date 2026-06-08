using System.Collections.Generic;
using Kingmaker.PubSubSystem; // EventBus
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // ISpellbookHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MemorizingPanel; // SpellbookMemorizeSlotVM
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One memorize slot (<see cref="SpellbookMemorizeSlotVM"/>, an AbilityDataVM) in a prepared caster's
    /// memorizing panel. Filled = the PREPARED spell (the slot's SpellShell); empty = an open slot you fill
    /// from the known-spell list (Enter on a known spell memorizes into the next free slot). A slot reads
    /// "needs rest" when it isn't castable right now (NeedRestToRestore = SpellShell present but !Available —
    /// i.e. cast/spent, or just re-prepared and awaiting rest); otherwise it's castable now. Enter on a
    /// filled slot forgets it (the game's double-click → ISpellbookHandler.TryForget). Empty slots carry no
    /// action.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyMemorizeSlot : UIElement
    {
        private readonly SpellbookMemorizeSlotVM _vm;

        public ProxyMemorizeSlot(SpellbookMemorizeSlotVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private bool Filled => _vm?.SpellData != null;

        private string Name() => Filled ? _vm.DisplayName : Message.Localized("ui", "spellbook.empty_slot").Resolve();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("spell");
            // Castable now vs prepared-but-pending-rest (spent, or re-prepared and not yet rested).
            if (Filled && _vm.NeedRestToRestore)
                yield return new ValueAnnouncement(Message.Localized("ui", "spellbook.needs_rest"));
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => Filled ? _vm.Tooltip : null;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Filled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "spellbook.forget"),
                    _ => EventBus.RaiseEvent<ISpellbookHandler>(h => h.TryForget(_vm)));
        }
    }
}
