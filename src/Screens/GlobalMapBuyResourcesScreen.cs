using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Kingdom; // KingdomResource, KingdomUIRoot
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.Crusade.Recruit; // RecruitBuyResourcesVM, RecruitBuyResourceItemVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The crusade's BUY RESOURCES window (Buy Resources in the Stats fold-out): party gold bought
    /// into finance and material points at fixed prices. On screen: two rows, each an icon, the
    /// count still affordable, a slider with -/+ buttons and a typed count, and the price; a
    /// "cost/money" line; Buy and Close; Escape and the veil close it. Here: one tab stop with the
    /// two counters (Left/Right step by one, the large step by ten, through the VM's SetBuyCount
    /// so the cross-clamping between the two counters is the game's), the total line, then Buy
    /// (enabled while the total is above zero, with the purchase sound the PC Buy plays) and
    /// Close. Pushed while the game holds the VM, popped when it disposes.
    /// </summary>
    public sealed class GlobalMapBuyResourcesScreen : Screen
    {
        public GlobalMapBuyResourcesScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_buy_resources";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.BuyResource;
        public override int Layer => 15; // a modal over the world-map base context

        // The host only DISPOSES the VM on close (its reference stays set), so a disposed one is "closed".
        private static RecruitBuyResourcesVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var vm = rc?.GlobalMapVM?.KingdomInfoVM?.BuyResourcesVM?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            if (Vm() == null) return;
            b.BeginStop("counters").PushContext(ScreenName, "group");
            b.AddItem(ControlId.Structural("buy:finance"), Counter(
                () => KingdomUIRoot.Instance.GetResourceElement(KingdomResource.Finances).Name, () => Vm()?.FinanceVM));
            b.AddItem(ControlId.Structural("buy:materials"), Counter(
                () => KingdomUIRoot.Instance.GetResourceElement(KingdomResource.Materials).Name, () => Vm()?.MaterialVM));
            b.AddItem(ControlId.Structural("buy:total"), GraphNodes.Text(() =>
            {
                var vm = Vm();
                return vm == null ? "" : Loc.T("crusade.buy_total", new { cost = vm.FinalCost.Value, money = vm.MoneyCount });
            }));
            b.PopContext();

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("buy:buy"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.BuyResource,
                () =>
                {
                    var vm = Vm();
                    if (vm == null || vm.FinalCost.Value <= 0) return;
                    UiSound.Play(UISoundType.ArmyManagementBuyResourcesPlay); // the PC Buy's sound
                    vm.Buy();
                },
                () => (Vm()?.FinalCost.Value ?? 0) > 0, sound: null));
            b.AddItem(ControlId.Structural("buy:close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
        }

        // One resource counter: "label, slider, N, M available, P gold each"; Left/Right adjust.
        private static NodeVtable Counter(Func<string> label, Func<RecruitBuyResourceItemVM> item)
        {
            Func<string> value = () =>
            {
                var it = item();
                return it == null ? "" : Loc.T("crusade.buy_counter", new { count = it.BuyCount.Value, available = it.AvailableCount.Value, cost = it.Cost });
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                StateText = value, // spoken after each step
                OnAdjust = (sign, large) =>
                {
                    var it = item();
                    if (it == null) return;
                    int before = it.BuyCount.Value;
                    it.SetBuyCount(before + sign * (large ? 10 : 1)); // the VM clamps to what gold allows
                    if (it.BuyCount.Value != before) UiSound.Play(UISoundType.ButtonClick); // the -/+ buttons' click
                },
            };
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }
    }
}
