using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root; // LocalizedTexts (filter labels)
using Kingmaker.UI.Common; // ItemsFilter (filter/sorter enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // InventoryVM, InventoryDollVM, EquipSlotVM
using Kingmaker.UI.MVVM._VM.Slots; // ItemSlotVM, ItemsFilterVM
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>), graph-native. Mirrors the game window's
    /// content: the character switcher, the same character-summary blocks the char sheet shows (via the
    /// shared <see cref="CharSheetBlocks"/> on a <see cref="GraphCharSheetSink"/>), the equipment doll
    /// (grip + weapon-set rows over the "Slot: item" list), the party-wide load/gold lines, and the
    /// shared stash — search, sort, filter row, then the Name/Type/Qty/Weight/Value table whose item
    /// rows carry the tooltip and inventory actions (Enter = the double-click quick action, Backspace =
    /// the full context menu). Everything renders live; stash rows key by SLOT IDENTITY, so equipping /
    /// using / dropping lands focus on a genuinely different row and the differ reads it — the old
    /// signature/capture/restore machinery is deleted. Doll/stats keys carry the viewed unit, so
    /// switching characters re-keys them (switcher focus survives). Escape closes.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public InventoryScreen() { Wrap = true; } // Tab cycles the window's stops end-around

        public override string Key => "service.Inventory";
        public override string ScreenName => Loc.T("screen.inventory");
        public override int Layer => 10;
        // Window-tab parity: the window.* hotkeys stay live inside this window (see UiAndWindows).
        public override System.Collections.Generic.IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories => UiAndWindows;

        public override bool IsActive()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return false;
            var cur = rc.CurrentServiceWindow; // Inventory/Equipment/SmartItem all open the InventoryVM window
            return cur == ServiceWindowsType.Inventory || cur == ServiceWindowsType.Equipment
                   || cur == ServiceWindowsType.SmartItem;
        }

        // Any keyboard-drag held item is dropped mentally, not mechanically, when the window opens or
        // closes — the item never left its slot until placed, so clearing state is all "drop" means.
        public override void OnPush() => ItemDrag.Clear();
        public override void OnPop() => ItemDrag.Clear();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows.Current?.HandleCloseAll());
        }

        private static InventoryVM Vm()
            => ServiceWindows.Current?.InventoryVM?.Value;



        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var unit = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value;
            string k = "inv:" + vm.GetHashCode() + ":";
            // Doll/stats keys carry the viewed unit: a character switch re-keys them (and re-homes into
            // the new character's content only if focus was inside it — switcher focus is untouched).
            string uk = k + (unit != null ? unit.CharacterName : "?") + ":";

            // Character switcher — the shared Tab-stop (ServiceWindows.EmitCharacterSwitcher):
            // drives the game's real selection; the current character carries a live "selected" part.
            ServiceWindows.EmitCharacterSwitcher(b, k);

            // Character summary — the same blocks as the char-sheet Summary page (shared renderer).
            b.BeginStop("stats");
            var sink = new GraphCharSheetSink(b, uk + "stats:");
            CharSheetBlocks.NamePortrait(vm.NameAndPortraitVM, sink);
            CharSheetBlocks.LevelClassScores(vm.LevelClassScoresVM, sink);
            CharSheetBlocks.Attacks(vm.AttacksBlockVM, sink);
            CharSheetBlocks.Defence(vm.DefenceBlockVM, sink);
            sink.Finish();

            BuildEquipment(b, vm.DollVM, uk);
            BuildLoad(b, vm.StashVM, k);
            BuildStash(b, vm, k);
            BuildFinnean(b, vm, k);
        }

        // Finnean, the talking weapon — the game's smart-item panel, as a Tab-stop after the stash.
        // Closed: the same toggle button the sighted window shows (game-localized "Finnean"); Enter
        // opens the panel via the VM contract (ToggleSmartItem creates the InventorySmartItemVM and,
        // for sighted players, swaps the stash panel out — parity). Open: Talk (hidden on the global
        // map, like the game), the weapon-form radios (Enter STAGES a form — the game's dropdown),
        // the live "who can wield" preview, then Equip (the slot's double-click) and Close. Staging
        // before equipping is deliberate parity: the preview reads the can-do warning BEFORE the
        // form is committed to the character.
        private static void BuildFinnean(GraphBuilder b, InventoryVM vm, string k)
        {
            if (vm.HasSmartItem?.Value != true) return;
            var strings = Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.CharacterSheet;
            b.BeginStop("finnean");

            var sm = vm.SmartItemVM?.Value;
            if (sm == null)
            {
                b.AddItem(ControlId.Structural(k + "finnean:open"),
                    GraphNodes.Button(() => (string)strings.SmartItemLabel, () => vm.ToggleSmartItem()));
                return;
            }

            // Open: each piece is its own Tab-stop — weapons list, then the staged form + wielder
            // preview, then Equip, then Close (user spec 2026-08-17).
            string fk = k + "finnean:" + sm.GetHashCode() + ":";

            if (sm.CanStartDialog.Value)
                b.AddItem(ControlId.Structural(fk + "talk"),
                    GraphNodes.Button(() => (string)strings.SmartItemStartDialog, () => sm.StartDialog()));

            if (sm.CanPolymorph.Value && sm.PolymorphItems != null)
            {
                b.BeginStop("finnean_forms").PushContext((string)strings.SmartItemLabel, "list");
                for (int i = 0; i < sm.PolymorphItems.Count; i++)
                {
                    var item = sm.PolymorphItems[i];
                    int idx = i;
                    var vt = GraphNodes.ChoiceOption(
                        () => item.Blueprint.NonIdentifiedName,
                        // Staged = what sits in the slot right now (the dropdown's selection).
                        () => sm.SmartItemSlotVM?.Item?.Value?.Blueprint == item.Blueprint,
                        () => sm.SelectItem(idx));
                    // The form's item tooltip (resolved fresh per press — never cache a template).
                    vt.OnTooltip = () => TooltipScreen.Open(
                        new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateItem(item));
                    b.AddItem(ControlId.Structural(fk + "form:" + i), vt);
                }
                b.PopContext();

                // The staged form + wielder preview: whether the current character can use it —
                // read it before committing with Equip.
                b.BeginStop("finnean_form");
                b.AddItem(ControlId.Structural(fk + "cando"), GraphNodes.Text(() =>
                {
                    var staged = sm.SmartItemSlotVM?.Item?.Value ?? sm.CurrentPolymorph;
                    if (staged == null) return Loc.T("finnean.no_form");
                    string name = staged.Blueprint?.NonIdentifiedName ?? staged.Name;
                    var warn = Kingmaker.UI.Common.UIUtilityTexts.GetCanDoText(staged, defaultColor: true);
                    return name + ", " + (string.IsNullOrEmpty(warn) ? Loc.T("finnean.can_wield") : warn);
                }));

                b.BeginStop("finnean_equip").AddItem(ControlId.Structural(fk + "equip"),
                    GraphNodes.Button(() => Loc.T("finnean.equip"), () => sm.SmartItemSlotVM?.Equip()));
            }

            b.BeginStop("finnean_close").AddItem(ControlId.Structural(fk + "close"),
                GraphNodes.Button(() => Loc.T("finnean.close"), () => vm.ToggleSmartItem()));
        }

        // The equipment doll: the grip button (only when the active set can re-grip), the weapon-set
        // row (radios), then the flat "Slot: item" list of the worn gear.
        private static void BuildEquipment(GraphBuilder b, InventoryDollVM doll, string uk)
        {
            if (doll == null) return;
            b.BeginStop("equipment").PushContext(Loc.T("inv.equipment"), "list");

            if (doll.WeaponSets != null && doll.WeaponSets.Count > 0)
            {
                // Grip toggle for the active set, above the sets (only emitted when it can re-grip —
                // mirroring the game's grip button, which is hidden otherwise).
                if (doll.CurrentSet?.Value != null && doll.CurrentSet.Value.CanToggleGrip.Value)
                    b.AddItem(ControlId.Structural(uk + "grip"), ItemNodes.GripToggle(() => doll.CurrentSet?.Value));
                b.StartRow();
                int wi = 0;
                foreach (var ws in doll.WeaponSets)
                {
                    if (ws != null)
                        b.AddItem(ControlId.Referenced(ws, uk + "set:" + wi), ItemNodes.WeaponSet(ws));
                    wi++;
                }
                b.EndRow();
            }

            var set = doll.CurrentSet?.Value;
            EmitSlot(b, uk, Loc.T("slot.primary_hand"), set?.Primary);
            // The off-hand reads the main hand's ghosted two-handed/double weapon instead of "empty"
            // (what the sighted doll shows — a falchion visibly occupies both hand slots).
            EmitSlot(b, uk, Loc.T("slot.secondary_hand"), set?.Secondary, set?.Primary);
            EmitSlot(b, uk, Loc.T("slot.armor"), doll.Armor);
            EmitSlot(b, uk, Loc.T("slot.head"), doll.Head);
            EmitSlot(b, uk, Loc.T("slot.neck"), doll.Neck);
            EmitSlot(b, uk, Loc.T("slot.shoulders"), doll.Shoulders);
            EmitSlot(b, uk, Loc.T("slot.wrist"), doll.Wrist);
            EmitSlot(b, uk, Loc.T("slot.gloves"), doll.Gloves);
            EmitSlot(b, uk, Loc.T("slot.belt"), doll.Belt);
            EmitSlot(b, uk, Loc.T("slot.ring1"), doll.Ring1);
            EmitSlot(b, uk, Loc.T("slot.ring2"), doll.Ring2);
            EmitSlot(b, uk, Loc.T("slot.feet"), doll.Feet);
            EmitSlot(b, uk, Loc.T("slot.glasses"), doll.Glasses);
            EmitSlot(b, uk, Loc.T("slot.shirt"), doll.Shirt);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    EmitSlot(b, uk, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i]);
            b.PopContext();
        }

        private static void EmitSlot(GraphBuilder b, string uk, string name, EquipSlotVM slot,
            EquipSlotVM primaryForFake = null)
        {
            if (slot != null)
                b.AddItem(ControlId.Structural(uk + "slot:" + name), ItemNodes.EquipSlot(name, slot, primaryForFake));
        }

        // Party-wide readout: carry weight + load status (with the encumbrance breakdown tooltip) and
        // gold. These live on the shared stash, between the per-character equipment and the stash list.
        private static void BuildLoad(GraphBuilder b, InventoryStashVM stash, string k)
        {
            if (stash == null) return;
            b.BeginStop("load").PushContext(Loc.T("inv.inventory"), "list");
            var enc = stash.EncumbranceVM;
            if (enc != null)
                b.AddItem(ControlId.Structural(k + "encumbrance"), GraphNodes.Text(
                    // Everything the sighted bar shows: current weight + status PLUS the light/
                    // medium/heavy LIMITS (the bar labels its thresholds — without them
                    // "Overloaded" gives no target to shed to; the stuck-at-a-random-encounter-
                    // exit repro, where the party had no way to hear how much was too much).
                    () => Loc.T("inv.encumbrance", new { value = enc.LoadWeight.Value
                        + (string.IsNullOrEmpty(enc.LoadStatus.Value) ? "" : ", " + enc.LoadStatus.Value)
                        + ", " + Loc.T("inv.enc_limits", new
                        { light = enc.Light.Value, medium = enc.Medium.Value, heavy = enc.Heavy.Value }) }),
                    () => stash.EncumbranceTooltip));
            b.AddItem(ControlId.Structural(k + "gold"),
                GraphNodes.Text(() => Loc.T("inv.gold", new { value = stash.Money.Value })));
            b.PopContext();
        }

        // The stash panel — ONE stop so Up/Down walks the controls and the table: search box on top,
        // then the sort combo, then the filter row, then the Name/Type/Qty/Weight/Value item table.
        private static void BuildStash(GraphBuilder b, InventoryVM vm, string k)
        {
            var stash = vm.StashVM;
            var group = stash?.ItemSlotsGroup;
            var filter = stash?.ItemsFilter;
            b.BeginStop("stash");

            EmitItemFilters(b, filter, group, k);

            // The item table: rows key by SLOT identity (equip/use/drop lands on a different identity →
            // announced); each row carries Type/Qty/Weight/Value as parts and as header-labelled cells.
            var sheet = new GraphSheet(b, k + "stash:");
            sheet.Region(Loc.T("inv.stash"),
                new[] { Loc.T("col.type"), Loc.T("col.qty"), Loc.T("col.weight"), Loc.T("col.value") });
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    Func<string> type = () => s.TypeName.Value;
                    Func<string> qty = () => s.Count.Value > 1 ? s.Count.Value.ToString() : "1";
                    Func<string> weight = () => Weight(s.Weight.Value);
                    Func<string> cost = () => s.Cost.Value.ToString();
                    // Identity = the ITEM ENTITY, not the slot VM — the game re-deals items into the
                    // positional slots on every sorted refresh (shared InventorySorter), so slot-keyed
                    // focus silently ended up on a different item after equip/drop/move re-sorts
                    // (same mechanism as the vendor tester repro; see VendorScreen.EmitTable).
                    sheet.Row(
                        ItemNodes.InventoryItem(s, new[]
                        {
                            new NodeAnnouncement(type),
                            new NodeAnnouncement(qty),
                        }, offHand: () => vm.DollVM?.CurrentSet?.Value?.Secondary),
                        (object)s.Item.Value ?? s, type, qty, weight, cost);
                }
            if (!any) sheet.Line(GraphNodes.Text(() => Loc.T("inv.no_items")));
            sheet.Finish();
        }

        // The game's stash filter bar: 8 category toggles ("Other" maps to NonUsable), in display order.
        /// <summary>The game's items-filter block as nodes — search field, sort combo, then the type
        /// filter radio row — over an <see cref="ItemsFilterVM"/>. Shared by the inventory stash and
        /// both sides of the vendor window (the same widget in the sighted UI). Null-safe: emits
        /// whatever parts the VM provides.</summary>
        internal static void EmitItemFilters(GraphBuilder b, ItemsFilterVM filter, SlotsGroupVM<ItemSlotVM> group, string k)
        {
            if (filter?.ItemsFilterSearchVM != null)
                b.AddItem(ControlId.Structural(k + "search"),
                    ItemNodes.ItemSearch(filter.ItemsFilterSearchVM, () => group?.SearchString?.Value));

            if (filter != null)
                b.AddItem(ControlId.Structural(k + "sort"), ItemNodes.ItemSorter(filter));

            if (filter?.SelectionFilterGroup?.EntitiesCollection != null)
            {
                b.PushContext(Loc.T("inv.filters"), "list");
                b.StartRow();
                foreach (var type in FilterOrder)
                {
                    var e = FindFilter(filter, type);
                    if (e != null)
                        b.AddItem(ControlId.Referenced(e, k + "filter:" + type),
                            GraphNodes.SelectionItem(e, () => LocalizedTexts.Instance.ItemsFilter.GetText(e.CurrentFilter)));
                }
                b.EndRow();
                b.PopContext();
            }
        }

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
