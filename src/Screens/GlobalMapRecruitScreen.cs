using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.Crusade.Recruit; // RecruitVM, RecruitUnitVM, RecruitResourceVM, RecruitResourcePriceVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips; // SimpleTooltip

namespace WrathAccess.Screens
{
    /// <summary>
    /// The crusade RECRUIT window (the map menu's Recruit button; also the toolbar's Recruit when no
    /// army exists, and the army window's). Recruitment always fills an army standing on the map's
    /// recruit point: the game picks the one given, else the selected one, else any there, else
    /// CREATES an empty one there. Tab stops, in the window's order: the target army (the same cart
    /// the map shows, plus previous/next army and the split-off "create army" button when offered),
    /// the unit pool (name, head count on offer, weekly growth, price per head; Enter opens the count
    /// picker, <see cref="GlobalMapRecruitCountScreen"/>; Space = the unit tooltip), mercenaries with
    /// their reroll when any are on offer, the three crusade resources with daily income and Buy
    /// Resources, and Close. Every action is the VM method the PC control is wired to, with the
    /// sound the PC view plays around it.
    /// </summary>
    public sealed class GlobalMapRecruitScreen : Screen
    {
        public GlobalMapRecruitScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_recruit";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.Recruit;
        public override int Layer => 15; // a modal over the world-map base context

        internal static RecruitVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var vm = rc?.GlobalMapVM?.RecruitVM?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;

            // ---- the army being filled ----
            var cart = vm.MainArmyCartVm.Value;
            if (cart != null && !cart.IsDisposed)
            {
                b.BeginStop("army").PushContext(cart.ArmyName.Value, "group");
                GlobalMapArmyNodes.CartRows(b, () => Vm()?.MainArmyCartVm.Value, "rc:", liveGeneral: true);
                if (vm.HavePrevArmy.Value)
                    b.AddItem(ControlId.Structural("rc:prev"), GraphNodes.Button(() => Loc.T("crusade.prev_army"), () => Vm()?.PrevArmy()));
                if (vm.HaveNextArmy.Value)
                    b.AddItem(ControlId.Structural("rc:next"), GraphNodes.Button(() => Loc.T("crusade.next_army"), () => Vm()?.NextArmy()));
                if (vm.CanCreateArmy.Value)
                    b.AddItem(ControlId.Structural("rc:create"), GraphNodes.Button(
                        () => UIStrings.Instance.ContextMenu.Split, () => Vm()?.CreateArmy()));
                b.PopContext();
            }

            // ---- the unit pool ----
            b.BeginStop("units").PushContext(Loc.T("crusade.units"), "list");
            int i = 0;
            foreach (var u in vm.Shop)
            {
                if (u == null) { i++; continue; }
                b.AddItem(ControlId.Referenced(u, "unit:" + i), Unit(u));
                i++;
            }
            b.PopContext();

            // ---- mercenaries (the block is hidden while none are on offer) ----
            if (vm.MercShop.Count > 0)
            {
                b.BeginStop("mercs").PushContext(ct.MercHeader, "list");
                int m = 0;
                foreach (var u in vm.MercShop)
                {
                    if (u != null) b.AddItem(ControlId.Referenced(u, "merc:" + m), Unit(u));
                    m++;
                }
                b.AddItem(ControlId.Structural("rc:reroll"), GraphNodes.Button(RerollLabel, () =>
                {
                    var v = Vm();
                    if (v == null || !v.CanReroll.Value) return;
                    UiSound.Play(UISoundType.MercRerollProgressSweep); // the PC reroll's sound
                    v.OnMercReroll(fast: false); // the view runs its search animation, then rerolls
                }, () => Vm()?.CanReroll.Value ?? false, sound: null));
                b.PopContext();
            }

            // ---- resources ----
            b.BeginStop("resources").PushContext(ct.ResourceHeader, "group");
            b.AddItem(ControlId.Structural("rc:fin"), GraphNodes.Text(() => ResourceLine(Vm()?.ResourceFinance)));
            b.AddItem(ControlId.Structural("rc:mat"), GraphNodes.Text(() => ResourceLine(Vm()?.ResourceMaterial)));
            b.AddItem(ControlId.Structural("rc:fav"), GraphNodes.Text(() => ResourceLine(Vm()?.ResourceDivineFavor)));
            b.AddItem(ControlId.Structural("rc:buy"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.BuyResource, () => Vm()?.BuyResources()));
            b.PopContext();

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("rc:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.Close()));
        }

        // "Footmen, 66 available, plus 67 per Week, 15 Finance Points[, locked]"; Enter = the Buy button.
        private static NodeVtable Unit(RecruitUnitVM u)
        {
            Func<string> label = () => UnitLabel(u);
            Func<bool> enabled = () => !u.IsDisposed && u.CanBuy.Value;
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[] { GraphNodes.LabelPart(label), GraphNodes.DisabledPart(enabled) },
                SearchText = () => u.Name.Value,
                OnActivate = () =>
                {
                    if (!enabled()) return;
                    UiSound.Play(UISoundType.ButtonClick);
                    u.OnRecruit(false); // opens the count picker (Shift-click's "buy all" is not offered)
                },
                OnTooltip = () =>
                {
                    var tpl = u.Tooltip.Value;
                    if (tpl != null) TooltipScreen.Open(tpl);
                },
            };
        }

        private static string UnitLabel(RecruitUnitVM u)
        {
            if (u == null || u.IsDisposed) return "";
            var parts = new List<string> { Loc.T("crusade.unit_line", new { name = u.Name.Value, count = u.Count.Value }) };
            if (u.Growth.Value > 0)
                parts.Add(Loc.T("crusade.growth", new { growth = u.Growth.Value, unit = (string)UIStrings.Instance.CharGen.Week }));
            var price = PriceText(u.priceVM);
            if (!string.IsNullOrEmpty(price)) parts.Add(price);
            if (u.NotRerollable.Value) parts.Add(Loc.T("crusade.locked"));
            return string.Join(", ", parts);
        }

        /// <summary>A price as the non-zero resources, "15 Finance Points, 2 Materials Points".</summary>
        internal static string PriceText(RecruitResourcePriceVM p)
        {
            if (p == null) return "";
            var parts = new List<string>();
            foreach (var r in new[] { p.ResourceFinance, p.ResourceMaterial, p.ResourceDivineFavor })
                if (r != null && r.Count.Value > 0)
                    parts.Add(Loc.T("crusade.price", new { count = r.Count.Value, name = r.DisplayName }));
            return string.Join(", ", parts);
        }

        private static string ResourceLine(RecruitResourceVM r)
        {
            if (r == null) return "";
            var line = Loc.T("worldmap.value_line", new { name = r.DisplayName, value = r.Count.Value });
            return r.Growth.Value > 0
                ? line + ", " + Loc.T("crusade.growth", new { growth = r.Growth.Value, unit = (string)UIStrings.Instance.CharGen.Day })
                : line;
        }

        private static string RerollLabel()
        {
            var vm = Vm();
            var ct = UIStrings.Instance.CrusadeTexts;
            if (vm == null) return ct.MercReroll;
            return vm.FreeRerolls.Value > 0
                ? ct.MercReroll + ", " + string.Format(ct.MercFreeRerollFormat, vm.FreeRerolls.Value)
                : ct.MercReroll + ", " + ct.MercPriceRerollFormat + " " + Loc.T("crusade.price", new { count = vm.MercResourceFinancePrice.Count.Value, name = vm.MercResourceFinancePrice.DisplayName });
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }

    /// <summary>
    /// The recruit COUNT PICKER (the panel a unit's Buy button raises): how many of that unit to
    /// hire, from one up to what the pool and the crusade's resources allow. A slider steps the
    /// count through the VM's own clamp (large step ten), reading the running price and what stays
    /// in the pool; Max; Recruit (the PC button's hire sound) and Close (which the game treats as
    /// recruiting none). A mercenary offer is all-or-nothing: no slider, no Max.
    /// </summary>
    public sealed class GlobalMapRecruitCountScreen : Screen
    {
        public GlobalMapRecruitCountScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_recruit_count";
        public override string ScreenName => Vm()?.RecruitName ?? UIStrings.Instance.CrusadeTexts.Recruit;
        public override int Layer => 16; // over the recruit window

        private static RecruitBlockVM Vm()
        {
            var vm = GlobalMapRecruitScreen.Vm()?.RecruitBlock.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            b.BeginStop("count");
            if (vm.OnlyOneCount.Value)
            {
                b.AddItem(ControlId.Structural("rb:offer"), GraphNodes.Text(Summary));
            }
            else
            {
                b.AddItem(ControlId.Structural("rb:count"), Counter());
                b.AddItem(ControlId.Structural("rb:max"), GraphNodes.Button(() => Loc.T("crusade.max"), () =>
                {
                    UiSound.Play(UISoundType.ArmyManagementSliderMaxValueButtonPlay); // the PC Max's sound
                    Vm()?.SetMax();
                }, sound: null));
            }

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("rb:recruit"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.Recruit, () =>
                {
                    UiSound.Play(UISoundType.ArmyManagementHireTroopsPlay); // the PC Buy's sound
                    Vm()?.Recruit();
                }, sound: null));
            b.AddItem(ControlId.Structural("rb:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.Close()));
        }

        private static string Summary()
        {
            var vm = Vm();
            if (vm == null) return "";
            return Loc.T("crusade.count_value", new
            {
                count = vm.Count.Value,
                max = vm.MaxAvailableCount,
                price = GlobalMapRecruitScreen.PriceText(vm.rightPriceVM),
                left = vm.LeftSideUnitVM.Count.Value,
            });
        }

        private static NodeVtable Counter()
        {
            Func<string> label = () => Loc.T("crusade.count");
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(Summary, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                StateText = Summary, // spoken after each step
                OnAdjust = (sign, large) =>
                {
                    var vm = Vm();
                    if (vm == null) return;
                    int before = vm.Count.Value;
                    vm.SetCount(before + sign * (large ? 10 : 1)); // the VM clamps 1..affordable
                    if (vm.Count.Value != before) UiSound.Play(UISoundType.ButtonClick); // the -/+ buttons' click
                },
            };
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }
}
