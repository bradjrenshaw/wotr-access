using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // EquipSlotVM
using Kingmaker.UI.MVVM._VM.Slots;                     // ItemSlotVM

namespace WrathAccess.UI
{
    /// <summary>
    /// Keyboard drag-and-drop for the inventory (the sighted mouse drag): Backslash on an item picks
    /// it up, Backslash on a destination places it — which reaches the drops the quick-equip router
    /// can't express (a second greatsword into a Titan Fighter's off hand, doll-to-doll moves), because
    /// placing goes through the SLOT's own contract (<see cref="EquipSlotVM.InsertItem"/> →
    /// ItemSlot.CanInsertItem — the same wielder-aware validation the mouse drag uses), not the
    /// blueprint-routed <c>CharDoll.EquipItem</c>. A failed placement keeps the item held (try another
    /// slot); Backslash on the source or on stash-over-stash cancels; the screen clears any held state
    /// on open and close.
    /// </summary>
    internal static class ItemDrag
    {
        private static ItemSlotVM _source;  // the slot the held item lives in (stash row or doll slot)
        private static string _name;        // the held item's spoken name, captured at pickup

        public static bool Holding => _source != null && _source.HasItem;

        public static void Clear() { _source = null; _name = null; }

        /// <summary>Backslash on a stash row: pick its item up, or — holding something — return the
        /// held item to the stash (take off, when it came from a doll slot).</summary>
        public static void OnStashItem(ItemSlotVM slot)
        {
            if (!Holding)
            {
                if (slot == null || !slot.HasItem) { Tts.Speak(Loc.T("drag.empty"), interrupt: true); return; }
                _source = slot;
                _name = slot.DisplayName.Value ?? slot.Item.Value?.Name;
                Tts.Speak(Loc.T("drag.picked", new { name = _name }), interrupt: true);
                return;
            }
            if (ReferenceEquals(_source, slot)) { Cancel(); return; }
            if (_source is EquipSlotVM equipped)
            {
                var name = _name;
                var item = equipped.Item.Value;
                if (equipped.TryUnequip())
                {
                    // The vanilla unequip-to-stash sound (InventorySlotsController.TryUnequipItem).
                    Kingmaker.UI.UISoundController.Instance?.PlayItemSound(Kingmaker.UI.SlotAction.Put, item, equipSound: false);
                    Clear(); Refresh();
                    Tts.Speak(Loc.T("drag.to_stash", new { name }), interrupt: true);
                }
                else Tts.Speak(Loc.T("drag.cant_place"), interrupt: true);
                return;
            }
            // Stash onto stash: order is the sorter's business — treat as putting it back.
            Cancel();
        }

        /// <summary>Backslash on a doll slot: place the held item INTO this specific slot (the drag
        /// semantics — swaps out an occupant, validated by the slot itself), or pick up its item.</summary>
        public static void OnEquipSlot(EquipSlotVM slot, string slotName)
        {
            if (slot == null) return;
            if (!Holding)
            {
                if (!slot.HasItem) { Tts.Speak(Loc.T("drag.empty"), interrupt: true); return; }
                _source = slot;
                _name = slot.DisplayName.Value ?? slot.Item.Value?.Name;
                Tts.Speak(Loc.T("drag.picked", new { name = _name }), interrupt: true);
                return;
            }
            if (ReferenceEquals(_source, slot)) { Cancel(); return; }

            var item = _source.Item.Value;
            var name = _name;
            if (item == null) { Cancel(); return; }

            // Doll → doll: the destination validates against the CURRENT state, where the item still
            // occupies its source hand (off-hand → main hand reads as uninsertable) — the mouse drag
            // implicitly removes first. Take it off, place it, and put it back if the place refuses.
            if (_source is EquipSlotVM fromSlot)
            {
                if (!fromSlot.TryUnequip()) { Tts.Speak(Loc.T("drag.cant_place"), interrupt: true); return; }
                if (!slot.InsertItem(item))
                {
                    fromSlot.InsertItem(item); // rollback to where it came from
                    Tts.Speak(Loc.T("drag.cant_place"), interrupt: true);
                    return;
                }
            }
            else if (!slot.InsertItem(item))
            {
                Tts.Speak(Loc.T("drag.cant_place"), interrupt: true); // stays held — try another slot
                return;
            }

            // The vanilla equip sound (InventorySlotsController.TryEquipItem).
            Kingmaker.UI.UISoundController.Instance?.PlayItemSound(Kingmaker.UI.SlotAction.Put, item, equipSound: true);
            Clear(); Refresh();
            Tts.Speak(Loc.T("drag.placed", new { name, slot = slotName }), interrupt: true);
        }

        private static void Cancel() { Clear(); Tts.Speak(Loc.T("drag.cancelled"), interrupt: true); }

        private static void Refresh()
            => Kingmaker.PubSubSystem.EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
    }
}
