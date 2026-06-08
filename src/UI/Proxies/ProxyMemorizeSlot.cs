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
    /// memorizing panel. Filled = the memorized spell (with "used" when it's been cast and needs rest);
    /// empty = an open slot you fill from the known-spell list (Enter on a known spell memorizes into the
    /// next free slot). Enter on a filled slot forgets it (the game's double-click → ISpellbookHandler.
    /// TryForget). Empty slots carry no action.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyMemorizeSlot : UIElement
    {
        private readonly SpellbookMemorizeSlotVM _vm;

        public ProxyMemorizeSlot(SpellbookMemorizeSlotVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private bool Filled => _vm?.SpellData != null;

        private string Name()
        {
            if (!Filled) return Message.Localized("ui", "spellbook.empty_slot").Resolve();
            var n = _vm.DisplayName;
            if (_vm.NeedRestToRestore) n += " (" + Message.Localized("ui", "spellbook.used").Resolve() + ")";
            return n;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("spell");
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
