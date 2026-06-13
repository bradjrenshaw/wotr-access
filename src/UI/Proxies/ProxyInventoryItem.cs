using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;          // UIStrings.ContextMenu
using Kingmaker.PubSubSystem;                      // EventBus
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // IInventoryHandler
using Kingmaker.UI.MVVM._VM.Slots;                 // ItemSlotVM, INewSlotsHandler
using Owlcat.Runtime.UI.Tooltips;                  // TooltipBaseTemplate
using WrathAccess.Screens;                         // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One stash item — the Name cell (row label) of a row in the inventory stash table. The icon-only
    /// game tile carries no text; all of it lives on <see cref="ItemSlotVM"/>, which we read directly.
    /// The cell announces the item name with its visible badges folded in (magic / notable / unusable /
    /// new), carries the item's tooltip (Space), and exposes the inventory actions: Enter is the
    /// double-click quick action (Equip for equipment, Use for usables), and the secondary key opens the
    /// full context menu (Equip / Use / Use while can / Copy scroll / Split / Talk / Drop / Information) as
    /// a submenu — each entry mirroring InventorySlotView's action + its live predicate. Actions go through
    /// the same EventBus contracts the view uses (IInventoryHandler / INewSlotsHandler → InventoryVM), so we
    /// don't reimplement equip/drop/split logic. Drops out of nav once the item leaves the slot.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyInventoryItem : UIElement
    {
        private readonly ItemSlotVM _slot;

        public ProxyInventoryItem(ItemSlotVM slot) { _slot = slot; }

        public override bool CanFocus => _slot != null && _slot.HasItem;

        private static UIContextMenu Menu => UIStrings.Instance.ContextMenu;

        private string Name()
        {
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            var flags = new List<string>();
            if (_slot.IsMagic.Value) flags.Add("magic");
            if (_slot.IsNotable.Value) flags.Add("notable");
            if (!_slot.CanUse.Value) flags.Add("unusable");
            if (_slot.NeedCheck.Value) flags.Add("new");
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("item");
        }

        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            // The slot packs comparison templates (the EQUIPPED items) first; the focused item's own
            // template is always LAST — same end the game's ShowInfo reads.
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Enter = the sighted double-click quick action (Equip equipment / Use usables); if neither
            // applies, fall through to the full context menu so Enter always does something useful.
            yield return new ElementAction(ActionIds.Activate, Message.Raw(Menu.Equip), _ =>
            {
                if (_slot.IsEquipment) Equip();
                else if (_slot.IsUsable) Use();
                else OpenMenu();
            });
            // Secondary = the whole context menu.
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.menu"), _ => OpenMenu());
        }

        // The live context-menu set — mirrors InventorySlotPCView.CreateContextMenu, each entry gated by the
        // same VM predicate, evaluated now (so a non-applicable action just isn't listed).
        private void OpenMenu()
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(_slot.IsEquipment, Menu.Equip, Equip);
            Add(_slot.IsUsable, Menu.Use, Use);
            Add(_slot.IsUsableWhileCan, Menu.UseWhileCan, UseWhileCan);
            Add(_slot.IsScroll, _slot.CopyItemLabel, CopyScroll);
            Add(_slot.IsPossibleSplit, Menu.Split, Split);
            Add(_slot.CanTalk, Menu.Use, _slot.StartDialog);
            Add(_slot.HasItem, Menu.Drop, Drop);
            Add(_slot.HasItem, Menu.Information, _slot.ShowInfo);

            if (labels.Count == 0) { Tts.Speak(Loc.T("menu.no_actions"), interrupt: true); return; }
            var actions = runs;
            ChoiceSubmenuScreen.Open(Name(), labels, -1, idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        // Action verbs, routed exactly like InventorySlotView (EventBus → InventoryVM / VM method + Refresh).
        private void Equip() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryEquip(_slot));
        private void Drop() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryDrop(_slot));
        private void Split() => EventBus.RaiseEvent<INewSlotsHandler>(h => h.HandleTrySplitSlot(_slot));
        private void Use() { _slot.UseItem(); Refresh(); }
        private void UseWhileCan() { _slot.UseItemWhileCan(); Refresh(); }
        private void CopyScroll() { _slot.CopyItem(); Refresh(); }
        private static void Refresh() => EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
    }
}
