using HarmonyLib;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.AbilityScores;
using Kingmaker.UnitLogic.Class.LevelUp;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Ability Scores (point-buy) phase. A live "points remaining" line, then a grid of the six
    /// abilities (rows) by Score / Modifier / Race bonus / Raise / Lower (columns). Score/Modifier/Race
    /// bonus are read-only (Score's Space opens the stat detail); Raise/Lower are the game's stepper
    /// buttons — they read the point cost and Activate to spend/refund, re-announcing the new score and
    /// remaining points. All numbers read the live point-buy state (synchronous with the step), not the
    /// LateUpdate-lagged reactives, so values are correct the instant you step. A race-bonus chooser
    /// (which stat gets the racial +2) appears below when the race offers one.
    /// </summary>
    public sealed class AbilityScoresPhaseContent : CharGenPhaseContent<CharGenAbilityScoresVM>
    {
        // The controller (and thus the live point-buy model) isn't exposed publicly on the phase.
        private static readonly System.Reflection.FieldInfo ControllerField =
            AccessTools.Field(typeof(CharGenPhaseBaseVM), "m_LevelUpController");

        private readonly LevelUpController _controller;
        private FlowSheet _sheet;
        private ListRegion _raceRegion; // present only when the race offers a bonus choice

        public AbilityScoresPhaseContent(CharGenAbilityScoresVM phase) : base(phase)
            => _controller = ControllerField?.GetValue(phase) as LevelUpController;

        // One FlowSheet, three regions (one Tab-stop): a status line, the abilities grid, and — when the
        // race offers a +2 choice — a racial-bonus selector. Arrows move cell-to-cell across all three;
        // Ctrl+Up/Down jump between regions.
        public override void Build(Container content)
        {
            _sheet = new FlowSheet();

            _sheet.List(null) // unlabelled status line (self-describing, single cell)
                .Item(new TextElement(() => Loc.T("chargen.points_remaining", new { value = Points() })));

            var table = _sheet.Table(Loc.T("section.ability_scores"), Loc.T("col.score"), Loc.T("col.modifier"), Loc.T("col.race_bonus"), Loc.T("col.raise"), Loc.T("col.lower"));
            foreach (var a in Phase.AbilityScoreAllocators)
            {
                if (a == null) continue;
                var alloc = a; // capture for the live closures
                table.Row(new TextElement(() => alloc.Name.Value), new UIElement[]
                {
                    // The game's "Scores"/Modifier/Race bonus are the live total (incl. racial), the
                    // ability modifier of that total, and the racial component — read on focus (settled),
                    // so the racial choice is reflected here.
                    new TextElement(() => alloc.StatValue.Value.ToString()),
                    new TextElement(() => Signed(alloc.Bonus.Value)),
                    new TextElement(() => alloc.RaceBonus.Value.HasValue ? Signed(alloc.RaceBonus.Value.Value) : ""),
                    new ProxyStepper(() => CostLabel(alloc, raise: true), () => CanAct(alloc, raise: true),
                        alloc.TryIncreaseValue, () => Summary(alloc), actionVerb: "raise"),
                    new ProxyStepper(() => CostLabel(alloc, raise: false), () => CanAct(alloc, raise: false),
                        alloc.TryDecreaseValue, () => Summary(alloc), actionVerb: "lower"),
                }, tooltip: () => alloc.TooltipTemplate()); // Space on any cell in the row → stat detail
            }

            UpdateRace();   // adds the race region if applicable
            _sheet.Reflow();
            content.Add(_sheet);
        }

        public override void Tick()
        {
            // Add/remove the race region as availability changes; reflow re-lays the matrix while keeping
            // the other regions' cells (so a focused ability cell survives the change).
            if (UpdateRace()) _sheet.Reflow();
        }

        // The race ability-bonus chooser only exists for races that let you pick where the +2 goes.
        // Returns true if the region set changed (caller reflows).
        private bool UpdateRace()
        {
            bool show = Phase.RaceBonusAvailable != null && Phase.RaceBonusAvailable.Value;
            bool has = _raceRegion != null && _sheet.HasRegion(_raceRegion);
            if (show && !has)
            {
                _raceRegion = new ListRegion("Racial ability bonus");
                _raceRegion.Item(new ProxySequentialSelector("Racial ability bonus",
                    () => Phase.RaceBonusSelector != null ? Phase.RaceBonusSelector.Value : null));
                _sheet.AddRegion(_raceRegion);
                return true;
            }
            if (!show && has)
            {
                _sheet.RemoveRegion(_raceRegion);
                _raceRegion = null;
                return true;
            }
            return false;
        }

        // Live reads from the point-buy model (falling back to the allocator's reactive only when not in
        // point-buy mode), so the grid never shows a stale value after a step.
        private StatsDistribution Dist => _controller?.State?.StatsDistribution;
        private bool PointBuy => Dist != null && Dist.Available;

        private int Points() => PointBuy ? Dist.Points : (_controller != null ? _controller.State.AttributePoints : 0);

        // The live point-buy score for one ability (base, pre-racial) — from StatsDistribution, which
        // updates synchronously with AddStatPoint (the allocator's reactive only catches up on LateUpdate).
        private int Score(CharGenAbilityScoreAllocatorVM a)
            => (PointBuy && Dist.StatValues.TryGetValue(a.StatType, out var v)) ? v : a.StatValue.Value;

        private bool CanAct(CharGenAbilityScoreAllocatorVM a, bool raise) => raise
            ? (PointBuy ? Dist.CanAdd(a.StatType) : (_controller != null && _controller.State.AttributePoints > 0))
            : (PointBuy ? Dist.CanRemove(a.StatType) : a.CanRemove.Value);

        // The point cost shown on the stepper — or, when you can't raise, why ("maximum" at 18, or
        // "N points, need M more"); lowering shows the refund ("minimum" at 7, or "N points back").
        private string CostLabel(CharGenAbilityScoreAllocatorVM a, bool raise)
        {
            if (raise)
            {
                if (Score(a) >= 18) return L("value.maximum");
                int cost = PointBuy ? Dist.GetAddCost(a.StatType) : 1;
                if (CanAct(a, raise: true)) return L("value.points", new { count = cost });
                int need = cost - Points();                 // tell them how short they are
                return L("value.points_need_more", new { count = cost, need = need > 0 ? need : 1 });
            }
            if (Score(a) <= 7) return L("value.minimum");
            int refund = PointBuy ? -Dist.GetRemoveCost(a.StatType) : 1; // GetRemoveCost is negative (points returned)
            return L("value.points_back", new { count = refund });
        }

        // Spoken after a step: the now-current total score (incl. racial), its modifier, and the
        // remaining pool — computed from the live base + racial so it's fresh the instant you step.
        private string Summary(CharGenAbilityScoreAllocatorVM a)
        {
            int total = Score(a) + (a.RaceBonus.Value ?? 0);
            int mod = (int)System.Math.Floor((total - 10) / 2.0);
            return L("value.ability_summary", new { name = a.Name.Value, total, mod = Signed(mod), points = Points() });
        }

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
        private static string L(string key) => Message.Localized("ui", key).Resolve();
        private static string L(string key, object vars) => Message.Localized("ui", key, vars).Resolve();
    }
}
