using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.UnitSettings;
using WrathAccess.Localization;

namespace WrathAccess.UI
{
    /// <summary>
    /// Keyboard drag-and-drop for the action bar (the sighted mouse drag, <c>ActionBarPCView</c>'s
    /// drag handlers): Backslash on a bar slot or on an entry of the Abilities / Spells / Items
    /// groups picks it up; Backslash on a bar slot — or on the "empty slot" element the HUD shows
    /// while something is held — places it there through the game's own
    /// <see cref="ActionBarVM.MoveSlot"/>: a bar-to-bar drop SWAPS the two slots, a group-to-bar drop
    /// COPIES the entry into the slot (the group keeps it), exactly like the mouse. Runtime-only
    /// entries (temporary abilities) can't be picked up, as in vanilla. Backslash on the source, on a
    /// group entry, or after the selected unit changed cancels. The held state survives leaving the
    /// HUD focus so you can pick up from a group and place on the bar in one pass.
    /// </summary>
    internal static class ActionBarDrag
    {
        private static ActionBarSlotVM _source;
        private static UnitEntityData _unit;
        private static string _name;

        public static bool Holding => _source != null && _source.MechanicActionBarSlot != null;
        public static void Clear() { _source = null; _unit = null; _name = null; }

        private static ActionBarVM Bar() => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ActionBarVM;

        /// <summary>Backslash on a slot node (bar slot or group entry).</summary>
        public static void OnSlot(ActionBarSlotVM vm)
        {
            var bar = Bar();
            var unit = bar?.SelectedUnit?.Value;
            if (vm == null || bar == null || unit == null) { Tts.Speak(Loc.T("drag.no_target"), interrupt: true); return; }
            if (!Holding)
            {
                var m = vm.MechanicActionBarSlot;
                if (m == null || m is MechanicActionBarSlotEmpty || m.IsBad()) { Tts.Speak(Loc.T("drag.empty"), interrupt: true); return; }
                if (vm.IsRuntimeOnly.Value) { Tts.Speak(Loc.T("drag.cant_pickup"), interrupt: true); return; }
                _source = vm; _unit = unit; _name = m.GetTitle();
                Tts.Speak(Loc.T("drag.picked", new { name = _name }), interrupt: true);
                return;
            }
            if (unit != _unit) { Cancel(); return; }        // the bar now shows another character
            if (ReferenceEquals(vm, _source)) { Cancel(); return; }
            if (vm.Index == -1) { Cancel(); return; }        // group entries aren't drop targets
            Place(bar, vm);
        }

        /// <summary>Backslash on the HUD's "empty slot" element: place the held entry at that index.</summary>
        public static void OnEmpty(int index)
        {
            var bar = Bar();
            var unit = bar?.SelectedUnit?.Value;
            if (!Holding) { Tts.Speak(Loc.T("drag.empty"), interrupt: true); return; }
            if (bar == null || unit == null || unit != _unit) { Cancel(); return; }
            if (index < 0 || index >= bar.Slots.Count) { Tts.Speak(Loc.T("drag.cant_place"), interrupt: true); return; }
            Place(bar, bar.Slots[index]);
        }

        private static void Place(ActionBarVM bar, ActionBarSlotVM target)
        {
            var name = _name;
            bar.MoveSlot(_source, target); // the game's drop: bar↔bar swaps, group→bar copies
            Clear();
            Tts.Speak(Loc.T("drag.placed", new { name, slot = SlotName(target.Index) }), interrupt: true);
        }

        private static void Cancel() { Clear(); Tts.Speak(Loc.T("drag.cancelled"), interrupt: true); }

        /// <summary>Delete on a bar slot: empty it (the game's ClearSlot — what dropping a dragged
        /// slot outside the bar does). Group entries have nothing to clear.</summary>
        public static void ClearSlot(ActionBarSlotVM vm)
        {
            var bar = Bar();
            var m = vm?.MechanicActionBarSlot;
            if (bar == null || vm == null || vm.Index == -1 || m == null || m is MechanicActionBarSlotEmpty)
            {
                Tts.Speak(Loc.T("delete.no_target"), interrupt: true);
                return;
            }
            var name = m.GetTitle();
            bar.ClearSlot(vm);
            if (ReferenceEquals(vm, _source)) Clear(); // a held slot that's been emptied has nothing to place
            Tts.Speak(Loc.T("actionbar.slot_cleared", new { name, slot = SlotName(vm.Index) }), interrupt: true);
        }

        /// <summary>"slot 7" for the main row, "row 2 slot 3" for the additional rows.</summary>
        public static string SlotName(int index)
        {
            int row = index / Exploration.ActionBarHotkeys.PerRow, slot = index % Exploration.ActionBarHotkeys.PerRow;
            return row == 0
                ? Loc.T("actionbar.slot_name", new { slot = slot + 1 })
                : Loc.T("actionbar.slot_name_row", new { row = row + 1, slot = slot + 1 });
        }

        /// <summary>The first empty bar index across the three hotkey rows (main row first), or -1.</summary>
        public static int FirstEmptyIndex(ActionBarVM bar)
        {
            if (bar == null) return -1;
            int rows = Exploration.ActionBarHotkeys.Rows * Exploration.ActionBarHotkeys.PerRow;
            for (int i = 0; i < rows && i < bar.Slots.Count; i++)
            {
                var m = bar.Slots[i].MechanicActionBarSlot;
                if (m == null || m is MechanicActionBarSlotEmpty) return i;
            }
            return -1;
        }
    }
}
