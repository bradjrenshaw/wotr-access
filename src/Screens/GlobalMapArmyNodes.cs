using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Armies.State; // SquadState
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.MVVM._VM.Crusade.Armies; // GlobalMapCrusadeArmyVM
using Kingmaker.UI.MVVM._VM.Crusade.ArmyInfo; // ArmyInfoArmyCartVM, ArmyInfoSquadVM
using Kingmaker.UI.MVVM._VM.GlobalMap; // GlobalMapVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips; // SimpleTooltip

namespace WrathAccess.Screens
{
    /// <summary>
    /// ARMY MODE on the world map (an army is selected — the crusade flag): the two panels the game
    /// fades in, as tab stops of <see cref="GlobalMapScreen"/> that exist only while the mode is on.
    /// <b>Armies</b> is the card list (one per crusader army: name, power, movement points, general,
    /// level-up marker; Enter = the card's select button, which also scrolls the camera). <b>The
    /// selected army's cart</b> reads its general, the five stat counters (each with the game's
    /// tooltip text on Space) and its squads (unit and head count; Space = the game's unit tooltip).
    /// Everything reads the live view models off the root UI context each render. Travel needs
    /// nothing here: the location panel already orders whichever traveler is selected.
    /// </summary>
    internal static class GlobalMapArmyNodes
    {
        private static GlobalMapVM Map()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM;
        }

        private static ArmyInfoArmyCartVM Cart()
        {
            var c = Map()?.ArmyInfoHUDVM.Value;
            return c != null && !c.IsDisposed ? c : null;
        }

        public static void Build(GraphBuilder b)
        {
            var controller = Game.Instance?.GlobalMapController;
            var map = Map();
            if (controller == null || map == null || !controller.ArmyMode.Value) return;

            var cards = map.ArmiesVM?.Armies;
            if (cards != null && cards.Count > 0)
            {
                b.BeginStop("armies").PushContext(Loc.T("worldmap.armies"), "list");
                for (int i = 0; i < cards.Count; i++)
                {
                    var card = cards[i];
                    if (card?.Army == null) continue;
                    b.AddItem(ControlId.Referenced(card.Army, "armycard"), Card(card));
                }
                b.PopContext();
            }

            var cart = Cart();
            if (cart == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;
            b.BeginStop("army").PushContext(cart.ArmyName.Value, "group");
            b.AddItem(ControlId.Structural("army:leader"), GraphNodes.Text(LeaderLine));
            b.AddItem(ControlId.Structural("army:mp"), GraphNodes.Text(
                () => Line(ct.MovementPointsTooltipHeader, Cart()?.SquadsVM.MovementPoints.Value),
                () => SimpleTooltip.Make(ct.MovementPointsTooltipHeader, ct.MovementPointsTooltipDescription)));
            b.AddItem(ControlId.Structural("army:morale"), GraphNodes.Text(
                () => Line(ct.MoraleTooltipHeader, Cart()?.SquadsVM.ArmyMorale.Value),
                () => SimpleTooltip.Make(ct.MoraleTooltipHeader, ct.MoraleTooltipDescription)));
            b.AddItem(ControlId.Structural("army:danger"), GraphNodes.Text(
                () => Line(ct.DangerTooltipHeader, Cart()?.SquadsVM.Danger.Value),
                () => SimpleTooltip.Make(ct.DangerTooltipHeader, ct.DangerTooltipDescription)));
            b.AddItem(ControlId.Structural("army:perception"), GraphNodes.Text(
                () => Line(ct.PerceptionTooltipHeader, Cart()?.SquadsVM.Perception.Value),
                () => SimpleTooltip.Make(ct.PerceptionTooltipHeader, ct.PerceptionTooltipDescription)));
            b.AddItem(ControlId.Structural("army:size"), GraphNodes.Text(
                () =>
                {
                    var s = Cart()?.SquadsVM;
                    return s == null ? "" : Loc.T("worldmap.size_line", new { name = (string)ct.ArmySizeTooltipHeader, current = s.ArmySizeCurrent.Value, max = s.ArmySizeMax.Value });
                },
                () => SimpleTooltip.Make(ct.ArmySizeTooltipHeader, ct.ArmySizeTooltipDescription)));

            // Squads: the 14 grid cells hold one VM each; a wide unit spans cells, so list each
            // SquadState once, in grid order.
            var seen = new HashSet<SquadState>();
            var squads = cart.SquadsVM.Squads;
            for (int i = 0; i < squads.Count; i++)
            {
                var vm = squads[i];
                var st = vm?.Squad;
                if (st == null || st.Unit == null || !seen.Add(st)) continue;
                var cell = vm; // capture per iteration
                b.AddItem(ControlId.Referenced(st, "squad"), GraphNodes.Text(
                    () => SquadLine(cell), () => cell.GetTooltip()));
            }
            b.PopContext();
        }

        // "Crusader Army I, Danger 12, Movement points 40, no general[, level up available], selected".
        private static NodeVtable Card(GlobalMapCrusadeArmyVM card)
        {
            Func<string> label = () => CardLabel(card);
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(() => Game.Instance?.GlobalMapController?.SelectedArmy == card.Army),
                },
                SearchText = () => card.Name.Value,
                OnActivate = () => card.OnSelectClick(), // the card's select button (its own sound + camera scroll)
            };
        }

        private static string CardLabel(GlobalMapCrusadeArmyVM card)
        {
            var ct = UIStrings.Instance.CrusadeTexts;
            var parts = new List<string>
            {
                card.Name.Value,
                Line(ct.DangerTooltipHeader, card.Power.Value),
                Line(ct.MovementPointsTooltipHeader, (int)Math.Floor(card.MovementPoints.Value)),
            };
            var leader = card.Army?.Data?.Leader != null ? card.ArmyLeader.Value?.Name.Value : null;
            parts.Add(string.IsNullOrEmpty(leader)
                ? Loc.T("worldmap.no_general")
                : Loc.T("worldmap.value_line", new { name = (string)ct.GeneralLabelText, value = leader }));
            if (card.IsLevelUp.Value) parts.Add(Loc.T("worldmap.level_up"));
            return string.Join(", ", parts);
        }

        private static string LeaderLine()
        {
            var cart = Cart();
            if (cart == null) return "";
            var l = cart.LeaderVM;
            if (l == null || !l.HasLeader.Value) return Loc.T("worldmap.no_general");
            return Loc.T("worldmap.leader_line", new
            {
                label = (string)UIStrings.Instance.CrusadeTexts.GeneralLabelText,
                name = l.CommonVM?.Name.Value ?? "",
                level = l.Level.Value,
            });
        }

        private static string SquadLine(ArmyInfoSquadVM cell)
        {
            var st = cell?.Squad;
            if (st == null || st.Unit == null) return "";
            return Loc.T("worldmap.squad", new { name = (string)st.Unit.CharacterName, count = st.Count });
        }

        private static string Line(string name, int? value)
            => Loc.T("worldmap.value_line", new { name, value = value ?? 0 });
    }
}
