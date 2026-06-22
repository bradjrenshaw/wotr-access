using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;          // UIStrings (vendor button labels)
using Kingmaker.UI.MVVM._VM.Slots;                 // ItemSlotVM, SlotsGroupVM
using Kingmaker.UI.MVVM._VM.Vendor;                // VendorVM, VendorOptionsItemVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The vendor / trade window (<see cref="VendorVM"/>, an in-game static-part reactive). Replicates the
    /// game's trade as four browsable Tab-stop tables — Your inventory, Store, Buy cart, Sell cart — plus an
    /// Actions tab stop. The trade engine is <c>VendorLogic</c>: an item moves with one call,
    /// <see cref="ItemSlotVM.VendorTryMove"/>, which routes by the slot's own collection (stock→buy,
    /// inventory→sell, cart→return), so a single <see cref="ProxyVendorSlot"/> serves every region (Enter
    /// moves one, the context menu moves all / shows info). Each cart sheet carries its own "return all"
    /// button at the bottom; Deal/Mass sale/Mass-sell settings/Close live in the Actions list. The four
    /// tables refill on any change (capture/restore focus like the inventory window); the Actions list is a
    /// stable sibling so its focus survives a move. Escape closes. Selling equipped gear (the per-character
    /// doll) and the redundant item-info toggle are a later slice.
    /// </summary>
    public sealed class VendorScreen : Screen
    {
        public override string Key => "ctx.vendor";
        public override string ScreenName => Vm()?.VendorName?.Value ?? Loc.T("screen.vendor");
        public override int Layer => 15; // above contexts + service windows, same family as loot/dialogue
        public override bool IsActive() => Vm() != null;

        private Container _content;        // the four refilling tables
        private VendorVM _builtVm;         // shell rebuilds when the trade VM is recreated
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel;  // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _builtVm = null; _sig = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; _builtVm = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            bool fresh = !_built || vm != _builtVm;
            if (fresh) { _builtVm = vm; BuildShell(vm); }
            var sig = ContentSig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null;
            // Attach AFTER the first content fill, so initial focus lands on the first table (Your inventory).
            // Attaching inside BuildShell would run BuildInitialFocus while _content is still empty (the tables
            // build in RefillContent), dropping focus onto a stable sibling (the Deal list) instead.
            if (fresh) Navigation.Attach(this);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }

        private static VendorVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.VendorVM?.Value;

        private static Kingmaker.Items.VendorLogic Logic => Game.Instance?.Vendor;

        // Refill when any of the four collections, the deal total, or the player's gold changes.
        private static string ContentSig(VendorVM vm)
        {
            var sb = new StringBuilder();
            sb.Append(vm.StashVM?.Money?.Value ?? 0).Append('|').Append(vm.DealPrice.Value).Append('|');
            AppendGroup(sb, vm.StashVM?.ItemSlotsGroup);
            AppendGroup(sb, vm.VendorSlotsGroup);
            AppendGroup(sb, vm.VendorExchangePart);
            AppendGroup(sb, vm.PlayerExchangePart);
            return sb.ToString();
        }

        private static void AppendGroup(StringBuilder sb, SlotsGroupVM<ItemSlotVM> group)
        {
            sb.Append('[');
            if (group?.VisibleCollection != null)
                foreach (var s in group.VisibleCollection)
                    if (s != null && s.HasItem) sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            sb.Append(']');
        }

        private void BuildShell(VendorVM vm)
        {
            _built = true;
            _sig = null; // force RefillContent on this fresh build (before OnUpdate attaches)
            Clear();
            _content = new Panel();
            Add(_content);          // the four trade tables (refilled)
            // Stable siblings AFTER the tables, so their focus survives a table refill. Each is its own
            // labeled Tab-stop: bulk-sell (5), deal (6), close (7). (Attach happens in OnUpdate, after fill.)
            BuildBulkSell(vm);
            BuildDeal(vm);
            Add(new ProxyActionButton(Loc.T("vendor.close"), () => true, () => vm.Close(), actionVerb: "close"));
        }

        private void RefillContent(VendorVM vm)
        {
            if (_content == null) return;
            var cap = CaptureFocus();
            _content.Clear();

            // Your inventory — your gold reads first (a row before the table), then the sellable items.
            BuildTable(vm.StashVM?.ItemSlotsGroup, Loc.T("vendor.your_inventory"), VendorSide.Inventory, buy: false,
                lead: new TextElement(() => Loc.T("vendor.gold", new { value = vm.StashVM?.Money?.Value ?? 0 })), returnBtn: null);
            BuildTable(vm.VendorSlotsGroup, Loc.T("vendor.store"), VendorSide.Stock, buy: true, lead: null, returnBtn: null);
            BuildTable(vm.VendorExchangePart, Loc.T("vendor.buy_cart"), VendorSide.BuyCart, buy: true, lead: null,
                returnBtn: new ProxyActionButton(() => Strip(UIStrings.Instance.Vendor.ReturnBuy),
                    () => vm.CanVendorExchangeReturn.Value, () => vm.ReturnBuy()));
            BuildTable(vm.PlayerExchangePart, Loc.T("vendor.sell_cart"), VendorSide.SellCart, buy: false, lead: null,
                returnBtn: new ProxyActionButton(() => Strip(UIStrings.Instance.Vendor.ReturnSell),
                    () => vm.CanPlayerExchangeReturn.Value, () => vm.ReturnSale()));

            RestoreFocus(cap);
        }

        // One trade region as a labeled Tab-stop (the FlowSheet label is what tabbing announces): an optional
        // lead row (your gold, on the inventory), a Name/Type/Qty/Price table (Price = buy or sell per side),
        // and an optional trailing "return all" button (the cart sheets).
        private void BuildTable(SlotsGroupVM<ItemSlotVM> group, string label, VendorSide side, bool buy,
            UIElement lead, ProxyActionButton returnBtn)
        {
            var sheet = new FlowSheet(label);
            if (lead != null) sheet.List(null).Item(lead);
            var priceCol = Loc.T(buy ? "vendor.col_buy" : "vendor.col_sell");
            var items = sheet.Table(null, Loc.T("col.type"), Loc.T("col.qty"), priceCol);
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    items.Row(new ProxyVendorSlot(s, side), new UIElement[]
                    {
                        new TextElement(() => s.TypeName.Value),
                        new TextElement(() => s.Count.Value > 1 ? s.Count.Value.ToString() : "1"),
                        new TextElement(() => Price(s, buy)),
                    });
                }
            if (!any) items.Row(new TextElement(() => Loc.T("vendor.empty")), new UIElement[0]);
            items.Associate(0);

            if (returnBtn != null) sheet.List(null).Item(returnBtn); // "return all" at the bottom of the cart

            sheet.Reflow();
            _content.Add(sheet);
        }

        // Tab stop 5 — Bulk sell: the three category toggles that decide what Mass sale dumps (Masterwork /
        // Non-magical / Gems & animal parts, read live off the always-present OptionsVM), then the Mass sale
        // button. Labels + states are live, so it never needs rebuilding on a move.
        private void BuildBulkSell(VendorVM vm)
        {
            var list = new ListContainer(Loc.T("vendor.bulk_sell"));
            if (vm.OptionsVM?.ItemVms != null)
                foreach (var opt in vm.OptionsVM.ItemVms)
                {
                    if (opt == null) continue;
                    var o = opt;
                    list.Add(new ProxyBoolToggle(o.Title.Value, () => o.State.Value, () => o.SwitchOption()));
                }
            list.Add(new ProxyActionButton(() => Strip(UIStrings.Instance.Vendor.MassSell), () => true, () => vm.MassSale()));
            Add(list);
        }

        // Tab stop 6 — Deal: the running deal total (you receive / you pay) then the Deal button (only
        // enabled when the deal is affordable). Both live, so no rebuild on a move.
        private void BuildDeal(VendorVM vm)
        {
            var list = new ListContainer(Loc.T("vendor.deal_section"));
            list.Add(new TextElement(() => DealSummary(vm)));
            list.Add(new ProxyActionButton(() => Strip(UIStrings.Instance.Vendor.Deal),
                () => vm.IsPossibleDeal.Value, () => { vm.Deal(); AnnounceDealt(vm); }));
            Add(list);
        }

        private static string Price(ItemSlotVM s, bool buy)
        {
            var item = s.Item.Value;
            if (item == null) return "";
            var v = Logic;
            long p = v == null ? s.Cost.Value : (buy ? v.GetItemBuyPrice(item) : v.GetItemSellPrice(item));
            return p.ToString();
        }

        // DealPrice is the net: positive = you receive, negative = you pay.
        private static string DealSummary(VendorVM vm)
        {
            long d = vm.DealPrice.Value;
            if (d > 0) return Loc.T("vendor.deal_receive", new { value = d });
            if (d < 0) return Loc.T("vendor.deal_pay", new { value = -d });
            return Loc.T("vendor.deal_none");
        }

        private static void AnnounceDealt(VendorVM vm)
            => Tts.Speak(Loc.T("vendor.dealt", new { value = vm.StashVM?.Money?.Value ?? 0 }), interrupt: false);

        private static string Strip(Kingmaker.Localization.LocalizedString s)
            => TextUtil.StripRichText((string)s);

        // ---- focus survival across a refill (same pattern as InventoryScreen) ----

        private (int child, int row, int col) CaptureFocus()
        {
            var cur = Navigation.Active?.Current;
            if (cur != null && _content != null)
                for (int i = 0; i < _content.Children.Count; i++)
                    if (_content.Children[i] is FlowSheet fs && fs.TryCoords(cur, out int r, out int c))
                        return (i, r, c);
            return (-1, 0, 0);
        }

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
    }
}
