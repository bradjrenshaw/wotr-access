using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Loot;   // LootVM
using Kingmaker.UI.MVVM._VM.Slots;  // ItemSlotVM
using Owlcat.Runtime.UI.Tooltips;   // TooltipBaseTemplate
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One lootable item in a loot window (<see cref="ItemSlotVM"/>). Announces the item name (+ count
    /// for stacks/gold) and carries the item's tooltip (Space). Activate "takes" it via the VM contract
    /// (<see cref="LootVM.HandleTryCollectLootSlot"/>), which collects the item, plays the loot sound, and
    /// auto-closes the window in the quick-loot modes when the last item is gone. Once collected the slot
    /// empties, so we drop out of navigation (<see cref="CanFocus"/>) — the next arrow lands on the next item.
    /// The slot VM populates its own reactives (name/count/tooltip) independent of the PC view we bypass.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyLootSlot : UIElement
    {
        private readonly LootVM _loot;
        private readonly ItemSlotVM _slot;

        public ProxyLootSlot(LootVM loot, ItemSlotVM slot) { _loot = loot; _slot = slot; }

        // Drops out of nav once taken (the item's gone) without needing the whole screen rebuilt.
        public override bool CanFocus => _slot != null && _slot.HasItem;

        // HandleTryCollectLootSlot plays its own LootCollectOne/LootCollectGold sound.
        public override Kingmaker.UI.UISoundType? ActivateSound => null;

        private string ItemLabel()
        {
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            int count = _slot.Count.Value;
            return count > 1 ? name + ", " + count : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(ItemLabel()));
            yield return new RoleAnnouncement("item");
        }

        // Live each time Space is pressed (per the tooltips-live-not-cached rule); first of the slot's list.
        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[0] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.take"),
                _ => _loot.HandleTryCollectLootSlot(_slot));
        }
    }
}
