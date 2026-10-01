using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root; // BlueprintRoot (calendar)
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.MVVM._VM.Crusade.ArmyInfo; // ArmyCartSetLeaderVM, ArmyCartBuyLeaderVM, ArmyLeaderInfoVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips; // SimpleTooltip

namespace WrathAccess.Screens
{
    /// <summary>
    /// A crusade GENERAL'S CARD as one tab stop, the way the set-general and hire-general panels
    /// lay their cards side by side: the stop is named after the general; inside, the level and
    /// experience line, one row per stat (Space = the hover tooltip the PC stat holder carries),
    /// magic reserves, one row per skill (Space = the skill's own tooltip), the reassignment
    /// cooldown when one is running, and LAST the card's single button with the game's label for
    /// that panel. Only that button acts, as on screen; the rows are the card's always-visible
    /// contents.
    /// </summary>
    internal static class GeneralNodes
    {
        /// <param name="k">Unique key prefix for this card's stop and rows.</param>
        /// <param name="buttonLabel">The card button's text for this panel (hire / assign).</param>
        /// <param name="honourCooldown">The set panel hides the button while the general is on
        /// reassignment cooldown; the hire panel's candidates have none.</param>
        public static void Card(GraphBuilder b, ArmyLeaderInfoVM l, string k, Func<string> buttonLabel, bool honourCooldown)
        {
            if (l == null || l.IsDisposed) return;
            var ct = UIStrings.Instance.CrusadeTexts;
            b.BeginStop(k).PushContext(l.CommonVM?.Name.Value ?? "", "group");

            b.AddItem(ControlId.Structural(k + ":level"), GraphNodes.Text(() => LevelLine(l)));
            b.AddItem(ControlId.Structural(k + ":atk"), GraphNodes.Text(
                () => Line(ct.AttackBonusTooltipHeader, l.StatsVM.AttackBonus.Value),
                () => SimpleTooltip.Make(ct.AttackBonusTooltipHeader, ct.AttackBonusTooltipDescription)));
            b.AddItem(ControlId.Structural(k + ":def"), GraphNodes.Text(
                () => Line(ct.DefenceBonusTooltipHeader, l.StatsVM.DefenseBonus.Value),
                () => SimpleTooltip.Make(ct.DefenceBonusTooltipHeader, ct.DefenceBonusTooltipDescription)));
            b.AddItem(ControlId.Structural(k + ":spell"), GraphNodes.Text(
                () => Line(ct.SpellBonusTooltipHeader, l.StatsVM.SpellStrength.Value),
                () => SimpleTooltip.Make(ct.SpellBonusTooltipHeader, ct.SpellBonusTooltipDescription)));
            b.AddItem(ControlId.Structural(k + ":size"), GraphNodes.Text(
                () => Line(ct.ArmySizeIncreaseTooltipHeader, l.StatsVM.ArmySize.Value),
                () => SimpleTooltip.Make(ct.ArmySizeIncreaseTooltipHeader, ct.ArmySizeIncreaseTooltipDescription)));
            b.AddItem(ControlId.Structural(k + ":inf"), GraphNodes.Text(
                () => Line(ct.InfirmaryTooltipHeader, l.StatsVM.Infirmary.Value),
                () => SimpleTooltip.Make(ct.InfirmaryTooltipHeader, ct.InfirmaryTooltipDescription)));
            b.AddItem(ControlId.Structural(k + ":mana"), GraphNodes.Text(
                () => Loc.T("worldmap.size_line", new { name = (string)ct.MagicReserves, current = l.StatsVM.CurrentMana.Value, max = l.StatsVM.MaxMana.Value })));

            var skills = l.SkillsVM.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var s = skills[i];
                if (s == null) continue;
                b.AddItem(ControlId.Referenced(s, k + ":skill:" + i), GraphNodes.Text(
                    () => s.Count.Value > 1 ? Loc.T("crusade.skill_count", new { name = s.Name.Value, count = s.Count.Value }) : s.Name.Value,
                    () => s.GetTooltipTemplate()));
            }

            bool cooling = honourCooldown && l.AssignmentCooldown.Value > TimeSpan.Zero;
            if (cooling)
                b.AddItem(ControlId.Structural(k + ":cooldown"), GraphNodes.Text(() => Loc.T("crusade.cooldown",
                    new { time = BlueprintRoot.Instance.Calendar.GetCompactPeriodString(l.AssignmentCooldown.Value) })));
            else // the PC card hides its button while the cooldown runs
                b.AddItem(ControlId.Structural(k + ":act"), GraphNodes.Button(buttonLabel, () => l.OnClick()));

            b.PopContext();
        }

        private static string LevelLine(ArmyLeaderInfoVM l)
        {
            var e = l.ExpLevel.Value;
            string level = Loc.T("crusade.level", new { level = l.Level.Value });
            return e.Last
                ? level + ", " + Loc.T("crusade.exp_last", new { current = e.Current })
                : level + ", " + Loc.T("crusade.exp", new { current = e.Current, max = e.Max });
        }

        private static string Line(string name, int value) => Loc.T("worldmap.value_line", new { name, value });
    }

    /// <summary>
    /// The SET GENERAL panel (the army cart's general slot, live in the recruit window): one card
    /// stop per general you own who leads no army (its button assigns; the game asks before
    /// swapping one out), then Recruit new general, Remove (only while the army has one; the game
    /// confirms), and Close. Escape closes.
    /// </summary>
    public sealed class GlobalMapSetGeneralScreen : Screen
    {
        public GlobalMapSetGeneralScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_set_general";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.SetLeaderHeader;
        public override int Layer => 16; // over the recruit window

        private static ArmyCartSetLeaderVM Vm()
        {
            var vm = GlobalMapRecruitScreen.Vm()?.SetLeaderVM.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;
            if (vm.LeadersVM.Count == 0)
            {
                b.BeginStop("none");
                b.AddItem(ControlId.Structural("sg:none"), GraphNodes.Text(() => Loc.T("crusade.no_generals")));
            }
            for (int i = 0; i < vm.LeadersVM.Count; i++)
                GeneralNodes.Card(b, vm.LeadersVM[i], "sg:" + i, () => ct.ArmyLeaderActionHiring, honourCooldown: true);

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("sg:hire"), GraphNodes.Button(() => ct.RecruitNewLeaderText, () => Vm()?.OnBuyLeader()));
            if (vm.HasLeader.Value)
                b.AddItem(ControlId.Structural("sg:clear"), GraphNodes.Button(() => ct.ClearLeaderText, () => Vm()?.OnClearLeader()));
            b.AddItem(ControlId.Structural("sg:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.OnClose()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.OnClose());
        }
    }

    /// <summary>
    /// The HIRE GENERAL panel (Recruit new general): one card stop per candidate (its button asks
    /// the panel to hire: the game then shows its own confirmation, or its not-enough-finances
    /// notice), then the price and your finances as the panel words them, and Close. Escape closes.
    /// </summary>
    public sealed class GlobalMapHireGeneralScreen : Screen
    {
        public GlobalMapHireGeneralScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_hire_general";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.BuyLeaderHeader;
        public override int Layer => 17; // over the set-general panel

        private static ArmyCartBuyLeaderVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var vm = rc?.GlobalMapVM?.BuyLeader?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;
            for (int i = 0; i < vm.Leaders.Count; i++)
                GeneralNodes.Card(b, vm.Leaders[i], "hg:" + i, () => ct.ArmyLeaderActionBuying, honourCooldown: false);

            b.BeginStop("cost");
            b.AddItem(ControlId.Structural("hg:cost"), GraphNodes.Text(
                () => TextUtil.StripRichText(string.Format(ct.BuyLeaderCostFormat, Vm()?.CostFinance.Value ?? 0))));
            b.AddItem(ControlId.Structural("hg:have"), GraphNodes.Text(
                () => TextUtil.StripRichText(string.Format(ct.BuyLeaderCurrentFinanceFormat, Vm()?.CurrentFinance.Value ?? 0))));

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("hg:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.OnClose()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.OnClose());
        }
    }
}
