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
        private Panel _racePanel;
        private bool _raceShown;

        public AbilityScoresPhaseContent(CharGenAbilityScoresVM phase) : base(phase)
            => _controller = ControllerField?.GetValue(phase) as LevelUpController;

        public override void Build(Container content)
        {
            content.Add(new TextElement(() => "Points remaining: " + Points()));

            var table = new Table("Ability scores");
            table.AddHeaderRow(new TextElement("Ability", "heading"), new UIElement[]
            {
                new TextElement("Score"), new TextElement("Modifier"),
                new TextElement("Race bonus"), new TextElement("Raise"), new TextElement("Lower"),
            });
            foreach (var a in Phase.AbilityScoreAllocators)
            {
                if (a == null) continue;
                var alloc = a; // capture for the live closures
                table.AddDataRow(new TextElement(() => alloc.Name.Value), new UIElement[]
                {
                    // The game's "Scores"/Modifier/Race bonus are the live total (incl. racial), the
                    // ability modifier of that total, and the racial component — read on focus (settled),
                    // so the racial choice is reflected here.
                    new TextElement(() => alloc.StatValue.Value.ToString()),
                    new TextElement(() => Signed(alloc.Bonus.Value)),
                    new TextElement(() => alloc.RaceBonus.Value.HasValue ? Signed(alloc.RaceBonus.Value.Value) : ""),
                    new ProxyAbilityStep(alloc, _controller, raise: true),
                    new ProxyAbilityStep(alloc, _controller, raise: false),
                }, rowTooltip: () => alloc.TooltipTemplate()); // Space on any cell in the row → stat detail
            }
            content.Add(table);

            _racePanel = new Panel();
            content.Add(_racePanel);
            FillRace();
        }

        public override void Tick()
        {
            bool show = Phase.RaceBonusAvailable != null && Phase.RaceBonusAvailable.Value;
            if (show != _raceShown) FillRace();
        }

        // The race ability-bonus chooser only exists for races that let you pick where the +2 goes.
        private void FillRace()
        {
            _raceShown = Phase.RaceBonusAvailable != null && Phase.RaceBonusAvailable.Value;
            if (_racePanel == null) return;
            _racePanel.Clear();
            if (!_raceShown) return;
            _racePanel.Add(new ProxySequentialSelector("Racial ability bonus",
                () => Phase.RaceBonusSelector != null ? Phase.RaceBonusSelector.Value : null));
        }

        // Live reads from the point-buy model (falling back to the allocator's reactive only when not in
        // point-buy mode), so the grid never shows a stale value after a step.
        private StatsDistribution Dist => _controller?.State?.StatsDistribution;
        private bool PointBuy => Dist != null && Dist.Available;

        private int Points() => PointBuy ? Dist.Points : (_controller != null ? _controller.State.AttributePoints : 0);

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
    }
}
