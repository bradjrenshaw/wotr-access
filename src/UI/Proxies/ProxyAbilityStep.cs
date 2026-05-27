using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.AbilityScores;
using Kingmaker.UnitLogic.Class.LevelUp;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A point-buy Raise/Lower cell for one ability (the game's two stepper buttons). Reads the live
    /// point cost — or, when you can't raise, why ("maximum" at 18, or "N points, need M more") — and
    /// Activate performs the step, re-announcing the new score + modifier + points remaining.
    ///
    /// All values come from the live <see cref="StatsDistribution"/> (which updates synchronously with
    /// AddStatPoint), NOT the allocator's reactive caches: those are refreshed on LateUpdate, so reading
    /// them right after a step (the activate re-announcement) would report the pre-change value.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(ValueAnnouncement), typeof(EnabledAnnouncement))]
    public sealed class ProxyAbilityStep : UIElement
    {
        private readonly CharGenAbilityScoreAllocatorVM _vm;
        private readonly LevelUpController _controller;
        private readonly bool _raise;

        public ProxyAbilityStep(CharGenAbilityScoreAllocatorVM vm, LevelUpController controller, bool raise)
        {
            _vm = vm;
            _controller = controller;
            _raise = raise;
        }

        public override bool ReannounceOnActivate => true;
        public override string Role => "button"; // a Raise/Lower stepper button (announced by the grid)

        private StatType Type => _vm.StatType;
        private StatsDistribution Dist => _controller?.State?.StatsDistribution;
        private bool PointBuy => Dist != null && Dist.Available;

        private int Score => (PointBuy && Dist.StatValues.TryGetValue(Type, out var v)) ? v : _vm.StatValue.Value;
        private int Points => PointBuy ? Dist.Points : (_controller != null ? _controller.State.AttributePoints : 0);
        private bool CanAct => _raise
            ? (PointBuy ? Dist.CanAdd(Type) : (_controller != null && _controller.State.AttributePoints > 0))
            : (PointBuy ? Dist.CanRemove(Type) : _vm.CanRemove.Value);

        private string CostLabel()
        {
            if (_raise)
            {
                if (Score >= 18) return "maximum";
                int cost = PointBuy ? Dist.GetAddCost(Type) : 1;
                if (CanAct) return cost + " points";
                int need = cost - Points;                 // tell them how short they are
                return cost + " points, need " + (need > 0 ? need : 1) + " more";
            }
            if (Score <= 7) return "minimum";
            int refund = PointBuy ? -Dist.GetRemoveCost(Type) : 1; // GetRemoveCost is negative (points returned)
            return refund + " points back";
        }

        // Spoken after a step: the now-current total score (incl. racial), its modifier, and the
        // remaining pool. Computed from the live base + racial so it's fresh the instant you step
        // (the allocator's own total reactive only catches up on LateUpdate).
        private string Summary()
        {
            int total = Score + (_vm.RaceBonus.Value ?? 0);
            int mod = (int)Math.Floor((total - 10) / 2.0);
            string ms = mod >= 0 ? "+" + mod : mod.ToString();
            return _vm.Name.Value + " " + total + ", " + ms + ", " + Points + " points left";
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(CostLabel()));   // shown on grid focus
            yield return new ValueAnnouncement(Message.Raw(Summary()));     // used by reannounce-on-activate
            yield return new EnabledAnnouncement(CanAct);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (CanAct)
                yield return new ElementAction(ActionIds.Activate, Message.Raw(_raise ? "Raise" : "Lower"),
                    _ => { if (_raise) _vm.TryIncreaseValue(); else _vm.TryDecreaseValue(); });
        }
    }
}
