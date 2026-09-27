using System;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Kingdom; // KingdomState, KingdomResource, KingdomStats, KingdomUIRoot
using Kingmaker.UI.Common; // UIUtility
using Kingmaker.UI.MVVM._VM.GlobalMap.Toolbar; // GlobalMapToolbarVM, GlobalMapToolbarSettingsVM
using Kingmaker.UI.MVVM._VM.Kingdom.KingdomInfo; // KingdomInfoVM
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateKingdomMorale
using Kingmaker.UI; // UISoundType
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map's always-visible TOOLBAR as one tab stop of <see cref="GlobalMapScreen"/>: the
    /// game's top strip (location name, date and clock, crusade morale, Skip Day, the party/army
    /// mode flags, Recruit, the toolbar settings dropdown) plus the crusade Stats fold-out beside it
    /// (reserve, the three resources, the four kingdom stats). Everything reads the game's LIVE view
    /// models each render (<see cref="GlobalMapToolbarVM"/> / <see cref="KingdomInfoVM"/> off the
    /// root UI context), mirrors the PC view's visibility rules (crusade-only rows, mode flags only
    /// with a crusader army, Recruit only when the mode flags are hidden), and drives the VM methods
    /// the PC buttons are wired to. The Stats panel is a toggle whose rows appear inline beneath it
    /// while open (a fold-out that never takes focus on screen); the gear's settings popup does take
    /// over, so it is its own modal screen.
    /// </summary>
    internal static class GlobalMapToolbarNodes
    {
        private static GlobalMapToolbarVM Toolbar()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapToolbarVM;
        }

        private static KingdomInfoVM Info()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.KingdomInfoVM;
        }

        public static void Build(GraphBuilder b)
        {
            var tb = Toolbar();
            if (tb == null) return;
            b.BeginStop("toolbar").PushContext(Loc.T("worldmap.toolbar"), "toolbar");

            // The strip's readouts, in screen order (left part: location; right: morale, clock, date).
            b.AddItem(ControlId.Structural("tb:location"), GraphNodes.Text(() => Toolbar()?.LocationName.Value ?? ""));
            b.AddItem(ControlId.Structural("tb:datetime"), GraphNodes.Text(() =>
            {
                var t = Toolbar();
                if (t == null) return "";
                var time = t.GameTime.Value;
                return Loc.T("worldmap.date_time", new { date = t.DateString.Value, time = $"{time.Hours:D2}:{time.Minutes:D2}" });
            }));

            bool crusade = tb.IsCrusadeEnabled.Value;
            if (crusade)
            {
                // "Morale: high" exactly as the strip renders it; Space = the game's morale tooltip.
                b.AddItem(ControlId.Structural("tb:morale"), GraphNodes.Text(MoraleLine,
                    () => (TooltipBaseTemplate)new TooltipTemplateKingdomMorale()));
                b.AddItem(ControlId.Structural("tb:skipday"), GraphNodes.Button(
                    () => UIStrings.Instance.CrusadeTexts.SkipDay, () => Toolbar()?.SkipDay()));
            }

            // The party/army flags: shown only with a crusader army on the map (the PC view hides the
            // block otherwise), one toggle whose live value is the current mode.
            if (tb.CanChangeArmyMode.Value)
                b.AddItem(ControlId.Structural("tb:armymode"), ArmyModeToggle());
            else if (CanAddArmy())
                b.AddItem(ControlId.Structural("tb:recruit"), GraphNodes.Button(
                    () => UIStrings.Instance.CrusadeTexts.Recruit, () => Toolbar()?.AddArmy()));

            // The gear: opens the settings popup, which is its own modal screen
            // (GlobalMapToolbarSettingsScreen) exactly as the panel takes over on screen.
            b.AddItem(ControlId.Structural("tb:options"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.ToolbarSettingsTitle,
                () => Toolbar()?.ShowOptions()));

            // The crusade Stats fold-out (the "Stats" button beside the strip): reserve, resources, stats.
            var info = Info();
            if (crusade && info != null)
            {
                b.AddItem(ControlId.Structural("tb:stats"), GraphNodes.Toggle(
                    () => KingdomUIRoot.Instance.Texts.SettlementStats,
                    () => Info()?.IsOn.Value ?? false,
                    () => Info()?.Toggle(),
                    announceOnActivate: true));
                if (info.IsOn.Value) BuildStatsPanel(b);
            }
            b.PopContext();
        }

        // The PC view's morale strip: "{MoraleTitle}: {band}", the band chosen by the same thresholds.
        private static string MoraleLine()
        {
            if (!UIUtility.IsCrusadeEnabled || KingdomState.Instance == null) return "";
            var ct = UIStrings.Instance.CrusadeTexts;
            int v = KingdomState.Instance.MoraleState.CurrentValue;
            string band = v >= 0 ? ct.Morale_20_0 : v >= -20 ? ct.Morale_0_20 : v >= -40 ? ct.Morale_20_40
                : v >= -60 ? ct.Morale_40_60 : v < -80 ? ct.Morale_80_100 : ct.Morale_60_80;
            return $"{ct.MoraleTitle}: {band}";
        }

        // The PC toolbar's Recruit ("+") block: shown INSTEAD of the mode flags while there is no army.
        private static bool CanAddArmy()
        {
            try
            {
                return KingdomState.Founded && !Kingmaker.Settings.SettingsRoot.Difficulty.AutoCrusade
                    && UIUtility.OnGlobalMapWithArmies;
            }
            catch { return false; }
        }

        // One toggle for the two mode flags: value = army mode; flipping plays the mode-switch sound
        // the console binding uses (the PC flags carry the same click sound).
        private static NodeVtable ArmyModeToggle()
        {
            Func<string> value = () => Loc.T(Toolbar()?.ArmyMode.Value == true ? "value.on" : "value.off");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => Loc.T("worldmap.army_mode")),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("worldmap.army_mode"),
                StateText = value,
                OnActivate = () =>
                {
                    var t = Toolbar();
                    if (t == null || !t.CanChangeArmyMode.Value) return;
                    UiSound.Play(UISoundType.GlobalMapModeSwitch);
                    t.ChangeArmyMode();
                },
            };
        }

        private static void BuildStatsPanel(GraphBuilder b)
        {
            var info = Info();
            if (info == null) return;
            var reserve = info.GlobalMapKingdomReserveVM;
            if (reserve != null && reserve.IsVisible.Value)
                b.AddItem(ControlId.Structural("tb:reserve"), GraphNodes.Text(
                    () => Loc.T("worldmap.value_line", new { name = UIStrings.Instance.CrusadeTexts.ReserveTitle, value = Info()?.GlobalMapKingdomReserveVM?.Value.Value ?? 0 }),
                    () => Info()?.GlobalMapKingdomReserveVM?.Tooltip.Value));
            var res = info.ResourcesVM;
            if (res != null)
                for (int i = 0; i < res.ResourceVms.Count; i++)
                {
                    int k = i;
                    b.AddItem(ControlId.Structural("tb:res:" + k), GraphNodes.Text(
                        () => { var r = ResourceAt(k); return r == null ? "" : Loc.T("worldmap.value_line", new { name = r.Name, value = r.DetailedValue.Value }); },
                        () => ResourceAt(k)?.Tooltip));
                }
            var stats = info.StatsVM;
            if (stats != null)
                for (int i = 0; i < stats.StatVms.Count; i++)
                {
                    int k = i;
                    b.AddItem(ControlId.Structural("tb:stat:" + k), GraphNodes.Text(
                        () => { var s = StatAt(k); return s == null ? "" : StatLine(s); },
                        () => StatAt(k)?.Tooltip));
                }
            // Buy Resources: opens the purchase window (its own modal screen, GlobalMapBuyResourcesScreen).
            b.AddItem(ControlId.Structural("tb:buy"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.BuyResource, () => Info()?.ShowBuyResources()));
        }

        private static KingdomResourceVM ResourceAt(int i)
        {
            var list = Info()?.ResourcesVM?.ResourceVms;
            return list != null && i < list.Count ? list[i] : null;
        }

        private static KingdomStatVM StatAt(int i)
        {
            var list = Info()?.StatsVM?.StatVms;
            return list != null && i < list.Count ? list[i] : null;
        }

        // The stats table row: name, roman rank, experience, and the next rank's requirement ("-" at max).
        private static string StatLine(KingdomStatVM s)
        {
            string rank = UIUtility.ArabicToRoman(s.Rank.Value);
            return s.RankIsMax.Value
                ? Loc.T("worldmap.stat_line_max", new { name = s.Name, rank, value = s.CurrentValue.Value })
                : Loc.T("worldmap.stat_line", new { name = s.Name, rank, value = s.CurrentValue.Value, next = s.NextRequiredValue.Value });
        }
    }
}
