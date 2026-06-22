using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Slots;  // ItemSlotVM
using Owlcat.Runtime.UI.Tooltips;   // TooltipBaseTemplate
using WrathAccess.Screens;          // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>Which trade region a vendor slot lives in — sets the spoken verb for the move (the move
    /// itself is direction-agnostic; <see cref="ItemSlotVM.VendorTryMove"/> routes by the slot's own
    /// collection: from stock it buys, from your inventory it sells, from a cart it returns).</summary>
    public enum VendorSide { Stock, Inventory, BuyCart, SellCart }

    /// <summary>
    /// One item in a vendor trade region (vendor stock / your inventory / buy cart / sell cart) — the Name
    /// cell of its table row, mirroring <see cref="ProxyInventoryItem"/>. A plain <see cref="ItemSlotVM"/>,
    /// so Enter moves ONE the right way for free (buy from stock, sell from inventory, return from a cart)
    /// via <c>VendorTryMove(false,false)</c>; the context menu moves ALL (<c>VendorTryMove(false,true)</c>)
    /// or shows item info. Carries the item's own tooltip (Space). Drops out of nav once the item leaves the
    /// slot, so the next arrow lands on the next item without a rebuild.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyVendorSlot : UIElement
    {
        private readonly ItemSlotVM _slot;
        private readonly VendorSide _side;

        public ProxyVendorSlot(ItemSlotVM slot, VendorSide side) { _slot = slot; _side = side; }

        public override bool CanFocus => _slot != null && _slot.HasItem;

        // VendorTryMove plays the game's own move sound.
        public override Kingmaker.UI.UISoundType? ActivateSound => null;

        private string Name()
        {
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            var flags = new List<string>();
            if (_slot.IsMagic.Value) flags.Add(Loc.T("item.magic"));
            if (_slot.IsNotable.Value) flags.Add(Loc.T("item.notable"));
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("item");
        }

        // Live each Space; the focused item's own template is LAST (comparison/equipped templates first).
        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Enter = move ONE (the slot routes by its collection: buy / sell / return).
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "vendor.move_one." + Verb),
                _ => _slot.VendorTryMove(state: false, all: false));
            // Secondary = the per-item menu (move all / information).
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.menu"), _ => OpenMenu());
        }

        private void OpenMenu()
        {
            var labels = new List<string> { Loc.T("vendor.move_all." + Verb), Loc.T("menu.information") };
            ChoiceSubmenuScreen.Open(Name(), labels, -1, idx =>
            {
                if (idx == 0) _slot.VendorTryMove(state: false, all: true); // move the whole stack
                else if (idx == 1) _slot.ShowInfo();
            });
        }

        // The verb key suffix per side: stock buys, inventory sells, either cart returns.
        private string Verb => _side == VendorSide.Stock ? "buy"
            : _side == VendorSide.Inventory ? "sell" : "return";
    }
}
