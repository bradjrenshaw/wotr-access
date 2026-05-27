using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Spells;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A spell in the chargen spell picker — a checkbox in a multi-select group (with a slot budget),
    /// so activating toggles it (the game's own OnClick is SetSelectedFromView(!IsSelected), gated on
    /// IsAvailable since AllowSwitchOff is true). Reads checked/disabled live: a spell already in the
    /// spellbook, or any unselected spell once the budget is full, is unavailable; selected spells stay
    /// available so they can be switched off. Used as the row header of the Spells table.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySpellItem : UIElement
    {
        private readonly CharGenSpellSelectorItemVM _vm;

        public ProxySpellItem(CharGenSpellSelectorItemVM vm) { _vm = vm; }

        private bool Available => _vm != null && _vm.IsAvailable.Value;
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override bool ReannounceOnActivate => true; // toggling flips it in place

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.DisplayName ?? ""));
            yield return new RoleAnnouncement("checkbox");
            yield return new ValueAnnouncement(Message.Raw(IsSelected ? "checked" : "unchecked"));
            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Available)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Toggle"),
                    _ => _vm.SetSelectedFromView(!_vm.IsSelected.Value));
        }
    }
}
