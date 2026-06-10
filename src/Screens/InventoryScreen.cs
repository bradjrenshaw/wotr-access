using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root; // LocalizedTexts (filter labels)
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.Common; // ItemsFilter (filter/sorter enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // InventoryVM, InventoryDollVM, EquipSlotVM
using Kingmaker.UI.MVVM._VM.Slots; // ItemSlotVM
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>). Mirrors the game window's content: the same
    /// character-summary blocks the char sheet shows (name/HP, level/race/scores/classes, attacks, defence —
    /// shared via <see cref="CharSheetBlocks"/>), the equipment doll, and the shared party stash. Presented
    /// as Tab-stops: a character switcher, the stats summary (one FlowSheet), the equipment as a
    /// [Slot, Item] table, and the stash as a Name/Type/Qty/Weight/Value table whose rows carry the item
    /// tooltip and the inventory actions (Equip/Use default on Enter, full context menu on the secondary
    /// key). The switcher is stable; the content refills when the viewed unit or the stash contents change,
    /// so tab focus survives. Filter/sort/search and the weapon-set swap are a later slice. Escape closes.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public override string Key => "service.Inventory";
        public override string ScreenName => Loc.T("screen.inventory");
        public override int Layer => 10;
        public override bool IsActive()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return false;
            var cur = rc.CurrentServiceWindow; // Inventory/Equipment/SmartItem all open the InventoryVM window
            return cur == ServiceWindowsType.Inventory || cur == ServiceWindowsType.Equipment
                   || cur == ServiceWindowsType.SmartItem;
        }

        private Container _content;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _sig = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell(vm);
            var sig = ContentSig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null; // settled: the next change is a fresh action, so announce its landing
        }

        // Re-focus the same grid position (clamped) in the rebuilt content: the row that index now holds —
        // the next item after an equip, the now-empty slot after an unequip. Announces the landing, but
        // suppresses the repeat when a virtualized collection settles over several frames onto the same row
        // (the burst keeps the same label until the sig stabilizes). Falls back to the content's first
        // focusable if that slot's gone (e.g. the stash emptied).
        private void RestoreFocus((int child, int row, int col) cap)
        {
            if (cap.child < 0) return;
            UIElement cell = null;
            if (cap.child < _content.Children.Count && _content.Children[cap.child] is FlowSheet fs && fs.RowCount > 0)
            {
                int r = Math.Min(cap.row, fs.RowCount - 1);
                int c = fs.Visitable(r, cap.col) ? cap.col : fs.LeftmostVisitable(r);
                if (c >= 0) cell = fs.CellAt(r, c);
            }
            cell = cell ?? _content.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static InventoryVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        // The content refills when the viewed unit changes (doll/stats) or the stash set changes
        // (equip/use/drop/collect). Stash order is included so a later sort/filter also refreshes.
        private static string ContentSig(InventoryVM vm)
        {
            var sb = new StringBuilder();
            sb.Append(Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value?.CharacterName).Append('|');
            // Weapon-set switch / grip toggle change the equipped hands without touching the stash, so fold
            // them in to trigger a rebuild (the hand slots are grabbed from the current set at build time).
            var set = vm.DollVM?.CurrentSet?.Value;
            if (set != null) sb.Append("set:").Append(set.Index).Append(':').Append((int)set.Grip.Value).Append('|');
            var vis = vm.StashVM?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var s in vis)
                    if (s != null && s.HasItem) sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            return sb.ToString();
        }

        private void BuildShell(InventoryVM vm)
        {
            _built = true;
            Clear();

            // Character switcher — switching the selected unit refills the doll + stats (the stash is the
            // shared party stash, unchanged). Drives the game's real selection.
            var party = Game.Instance?.Player?.Party;
            if (party != null && party.Count > 0)
            {
                var sw = new ListContainer(Loc.T("label.characters"));
                foreach (var u in party)
                {
                    var unit = u;
                    sw.Add(new ProxyActionButton(() => unit.CharacterName, () => true,
                        () => Game.Instance.SelectionCharacter.SetSelected(unit), actionVerb: "select"));
                }
                Add(sw);
            }

            _content = new Panel();
            Add(_content);
            Navigation.Attach(this);
        }

        private void RefillContent(InventoryVM vm)
        {
            if (_content == null) return;

            // The stash list is virtualized — its slot VMs are recycled and the collection is rebuilt on
            // every change (equip/use/drop). Rebuilding the tables would orphan the cursor (left on a stale
            // "item" row, or jumping), so capture where focus sits inside the content and restore it to the
            // same grid position afterwards. Focus elsewhere (the character switcher) is left untouched.
            var cap = CaptureFocus();

            _content.Clear();

            // Character summary — the same blocks as the char-sheet Summary page (shared renderer).
            var sink = new FlowSheetCharSheetSink();
            CharSheetBlocks.NamePortrait(vm.NameAndPortraitVM, sink);
            CharSheetBlocks.LevelClassScores(vm.LevelClassScoresVM, sink);
            CharSheetBlocks.Attacks(vm.AttacksBlockVM, sink);
            CharSheetBlocks.Defence(vm.DefenceBlockVM, sink);
            if (sink.Build() is FlowSheet stats && stats.RowCount > 0) _content.Add(stats);

            BuildEquipment(vm.DollVM);
            BuildLoad(vm.StashVM);
            BuildStash(vm);

            RestoreFocus(cap);
        }

        // (contentChildIndex, row, col) of the focused cell within one of the content FlowSheets, or
        // child = -1 when focus is outside the content (so we don't yank it).
        private (int child, int row, int col) CaptureFocus()
        {
            var cur = Navigation.Active?.Current;
            if (cur != null)
                for (int i = 0; i < _content.Children.Count; i++)
                    if (_content.Children[i] is FlowSheet fs && fs.TryCoords(cur, out int r, out int c))
                        return (i, r, c);
            return (-1, 0, 0);
        }

        // The equipment doll: a "Weapon sets" bar (each set a radio; Enter activates, secondary toggles
        // grip) over a flat "Slot: item" list of the worn gear. Weapon sets sit here because they drive the
        // hand slots below.
        private void BuildEquipment(InventoryDollVM doll)
        {
            if (doll == null) return;
            var sheet = new FlowSheet();

            if (doll.WeaponSets != null && doll.WeaponSets.Count > 0)
            {
                // Grip toggle for the active set, above the sets (only landable when the weapon can re-grip).
                sheet.Bar(Loc.T("inventory.grip")).Cell(new ProxyGripToggle(() => doll.CurrentSet?.Value));
                var sets = sheet.Bar(Loc.T("inv.weapon_sets"));
                foreach (var ws in doll.WeaponSets)
                    if (ws != null) sets.Cell(new ProxyWeaponSet(ws));
            }

            var list = sheet.List(Loc.T("inv.equipment"));
            var set = doll.CurrentSet?.Value;
            AddSlot(list, Loc.T("slot.primary_hand"), set?.Primary);
            AddSlot(list, Loc.T("slot.secondary_hand"), set?.Secondary);
            AddSlot(list, Loc.T("slot.armor"), doll.Armor);
            AddSlot(list, Loc.T("slot.head"), doll.Head);
            AddSlot(list, Loc.T("slot.neck"), doll.Neck);
            AddSlot(list, Loc.T("slot.shoulders"), doll.Shoulders);
            AddSlot(list, Loc.T("slot.wrist"), doll.Wrist);
            AddSlot(list, Loc.T("slot.gloves"), doll.Gloves);
            AddSlot(list, Loc.T("slot.belt"), doll.Belt);
            AddSlot(list, Loc.T("slot.ring1"), doll.Ring1);
            AddSlot(list, Loc.T("slot.ring2"), doll.Ring2);
            AddSlot(list, Loc.T("slot.feet"), doll.Feet);
            AddSlot(list, Loc.T("slot.glasses"), doll.Glasses);
            AddSlot(list, Loc.T("slot.shirt"), doll.Shirt);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    AddSlot(list, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i]);
            sheet.Reflow();
            if (sheet.RowCount > 0) _content.Add(sheet);
        }

        private static void AddSlot(ListRegion list, string name, EquipSlotVM slot)
        {
            if (slot != null) list.Item(new ProxyEquipSlot(name, slot));
        }

        // Party-wide readout: carry weight + load status (with the encumbrance breakdown tooltip) and gold.
        // These live on the shared stash, so they sit between the per-character equipment and the stash list.
        private void BuildLoad(InventoryStashVM stash)
        {
            if (stash == null) return;
            var sheet = new FlowSheet();
            var list = sheet.List(Loc.T("inv.inventory"));
            var enc = stash.EncumbranceVM;
            if (enc != null)
                list.Item(new TextElement(() => Loc.T("inv.encumbrance", new { value = enc.LoadWeight.Value
                        + (string.IsNullOrEmpty(enc.LoadStatus.Value) ? "" : ", " + enc.LoadStatus.Value) })),
                    tooltip: () => stash.EncumbranceTooltip);
            list.Item(new TextElement(() => Loc.T("inv.gold", new { value = stash.Money.Value })));
            sheet.Reflow();
            if (sheet.RowCount > 0) _content.Add(sheet);
        }

        // The stash panel — ONE FlowSheet so Up/Down walks the bars and the table: search box on top, then
        // the sort bar (the stash-swap button will join it later), then the filter toggle row, then the
        // Name/Type/Qty/Weight/Value item table. The Name cell carries the tooltip + actions; the other
        // cells share the item's row tooltip. Filter/sort/search write to the stash group's reactive props,
        // which our refill-on-change already reacts to.
        private void BuildStash(InventoryVM vm)
        {
            var stash = vm.StashVM;
            var group = stash?.ItemSlotsGroup;
            var filter = stash?.ItemsFilter;
            var sheet = new FlowSheet();

            if (filter?.ItemsFilterSearchVM != null)
                sheet.Bar(Loc.T("inventory.search")).Cell(new ProxyItemSearch(filter.ItemsFilterSearchVM, () => group?.SearchString?.Value));

            if (filter != null)
                sheet.Bar(Loc.T("inventory.sort")).Cell(new ProxyItemSorter(filter)); // the stash-swap button will join this bar later

            if (filter?.SelectionFilterGroup?.EntitiesCollection != null)
            {
                var bar = sheet.Bar(Loc.T("inv.filters"));
                foreach (var type in FilterOrder)
                {
                    var e = FindFilter(filter, type);
                    if (e != null) bar.Cell(new ProxySelectionItem(e,
                        () => LocalizedTexts.Instance.ItemsFilter.GetText(e.CurrentFilter)));
                }
            }

            // Associated-element table: column 0 is the item (ProxyInventoryItem — name + tooltip + actions),
            // the rest are value columns. Up/Down reads the item + columns; a value column leads with its
            // value; Enter/secondary/Space on any cell fall through to the item (so the row tooltip is the
            // item's own — no separate row tooltip needed).
            var items = sheet.Table(Loc.T("inv.stash"), Loc.T("col.type"), Loc.T("col.qty"), Loc.T("col.weight"), Loc.T("col.value"));
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    items.Row(new ProxyInventoryItem(s), new UIElement[]
                    {
                        new TextElement(() => s.TypeName.Value),
                        new TextElement(() => s.Count.Value > 1 ? s.Count.Value.ToString() : "1"),
                        new TextElement(() => Weight(s.Weight.Value)),
                        new TextElement(() => s.Cost.Value.ToString()),
                    });
                }
            if (!any) items.Row(new TextElement(() => Loc.T("inv.no_items")), new UIElement[0]);
            items.Associate(0); // column 0 (the item) is the row's element

            sheet.Reflow();
            _content.Add(sheet);
        }

        // The game's stash filter bar: 8 category toggles ("Other" maps to NonUsable), in display order.
        private static readonly ItemsFilter.FilterType[] FilterOrder =
        {
            ItemsFilter.FilterType.NoFilter, ItemsFilter.FilterType.Weapon, ItemsFilter.FilterType.Armor,
            ItemsFilter.FilterType.Accessories, ItemsFilter.FilterType.Ingredients, ItemsFilter.FilterType.Usable,
            ItemsFilter.FilterType.Notable, ItemsFilter.FilterType.NonUsable,
        };

        private static ItemsFilterEntityVM FindFilter(ItemsFilterVM filter, ItemsFilter.FilterType type)
        {
            foreach (var e in filter.SelectionFilterGroup.EntitiesCollection)
                if (e != null && e.CurrentFilter == type) return e;
            return null;
        }

        private static string Weight(float w) => w <= 0f ? "0" : w.ToString("0.#");
    }
}
