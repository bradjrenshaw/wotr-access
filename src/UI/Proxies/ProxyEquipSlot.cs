using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;          // UIStrings.ContextMenu
using Kingmaker.PubSubSystem;                      // EventBus
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // EquipSlotVM, IInventoryHandler
using Owlcat.Runtime.UI.Tooltips;                  // TooltipBaseTemplate
using WrathAccess.Screens;                         // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One equipment-doll slot (<see cref="EquipSlotVM"/>) as a list line — "Slot: item" (or "Slot: empty").
    /// Reads live from the VM: announces the slot name + equipped item with badges folded in, carries the
    /// item's tooltip, and exposes the equip-slot actions mirroring InventoryEquipSlotPCView.CreateContextMenu
    /// — Enter unequips (the double-click action), the secondary key opens the full menu (Take off / Use /
    /// Use while can / Copy scroll / Talk / Information). Unequip goes through the VM contract
    /// (<see cref="EquipSlotVM.TryUnequip"/>) + the game's Refresh event; empty slots carry no actions.
    /// (Equipping into an empty slot is done from the stash item's Equip action.)
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyEquipSlot : UIElement
    {
        private readonly string _slotName;
        private readonly EquipSlotVM _slot;

        public ProxyEquipSlot(string slotName, EquipSlotVM slot) { _slotName = slotName; _slot = slot; }

        public override bool CanFocus => true; // the slot is always shown, even when empty

        private static UIContextMenu Menu => UIStrings.Instance.ContextMenu;

        private bool HasItem => _slot != null && _slot.HasItem;

        private string ItemLabel()
        {
            if (!HasItem) return _slotName + ": empty";
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            var flags = new List<string>();
            if (_slot.IsMagic.Value) flags.Add("magic");
            if (_slot.IsNotable.Value) flags.Add("notable");
            if (_slot.CantRemove.Value) flags.Add("can't remove");
            if (flags.Count > 0) name += " (" + string.Join(", ", flags.ToArray()) + ")";
            return _slotName + ": " + name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(ItemLabel()));
            yield return new RoleAnnouncement("item");
        }

        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            if (!HasItem) return null;
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[0] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!HasItem) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw(Menu.TakeOff), _ => Unequip());
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.menu"), _ => OpenMenu());
        }

        private void OpenMenu()
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(_slot.IsEquipment, Menu.TakeOff, Unequip);
            Add(_slot.IsUsable, Menu.Use, Use);
            Add(_slot.IsUsableWhileCan, Menu.UseWhileCan, UseWhileCan);
            Add(_slot.IsScroll, _slot.CopyItemLabel, CopyScroll);
            Add(_slot.CanTalk, Menu.Use, _slot.StartDialog);
            Add(_slot.HasItem, Menu.Information, _slot.ShowInfo);

            if (labels.Count == 0) { Tts.Speak("No actions", interrupt: true); return; }
            var actions = runs;
            ChoiceSubmenuScreen.Open(ItemLabel(), labels, -1, idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        // Unequip via the VM contract; on success raise the same Refresh the view raises.
        private void Unequip() { if (_slot.TryUnequip()) Refresh(); }
        private void Use() { _slot.UseItem(); Refresh(); }
        private void UseWhileCan() { _slot.UseItemWhileCan(); Refresh(); }
        private void CopyScroll() { _slot.CopyItem(); Refresh(); }
        private static void Refresh() => EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
    }
}
