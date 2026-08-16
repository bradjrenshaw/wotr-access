using System;
using System.Collections.Generic;
using Kingmaker.ElementsSystem;           // ContextData (the scoped GameLogDisabled flag)
using Kingmaker.EntitySystem.Entities;   // UnitEntityData
using Kingmaker.PubSubSystem;             // EventBus, IDamageHandler, IUnitBuffHandler, IUnitHandler, IRulebookHandler
using Kingmaker.RuleSystem;               // RulebookEvent (DisableBattleLog)
using Kingmaker.RuleSystem.Rules;         // RuleDealStatDamage
using Kingmaker.RuleSystem.Rules.Damage;  // RuleDealDamage
using Kingmaker.UI.Models.Log;            // GameLogDisabled
using Kingmaker.UnitLogic.Buffs;          // Buff

namespace WrathAccess.Events
{
    /// <summary>
    /// The adapter layer: a persistent EventBus subscriber (like <see cref="WrathAccess.WarningReader"/>)
    /// turning raw game events into <see cref="ModEvent"/>s. Damage and healing fire per instance. Buffs are
    /// DE-NOISED — the game raises add/remove on every attach/detach (refreshes, re-applications,
    /// double-fires, and hidden system buffs), so we mirror its combat-log filter (skip
    /// <c>Blueprint.IsHiddenInUI</c> / empty-name buffs) and reconcile per frame against an active set:
    /// only a genuine gain (newly active) or loss (was active, not re-added this frame — i.e. not a
    /// refresh) is announced. Unit death (<see cref="IUnitHandler"/>) and ability damage/drain
    /// (<see cref="IRulebookHandler{T}"/> over RuleDealStatDamage) fire per instance, no de-noising.
    /// </summary>
    internal sealed class EventBusAdapter
        : IDamageHandler, IHealingHandler, IUnitBuffHandler, IUnitHandler, IGlobalRulebookHandler<RuleDealStatDamage>
    {
        private static EventBusAdapter _instance;

        // Buffs currently announced as active, keyed by (unit, blueprint).
        private readonly HashSet<BuffKey> _active = new HashSet<BuffKey>();
        // This frame's raw adds/removes (last Buff for a key wins), reconciled in Tick.
        private readonly Dictionary<BuffKey, Buff> _frameAdds = new Dictionary<BuffKey, Buff>();
        private readonly Dictionary<BuffKey, Buff> _frameRemoves = new Dictionary<BuffKey, Buff>();

        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new EventBusAdapter();
            EventBus.Subscribe(_instance);
        }

        /// <summary>Unhook from the game (module hot-reload teardown). EventBus.Unsubscribe also
        /// removes the global-rulebook registration Subscribe made.</summary>
        public static void Shutdown()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
        }

        /// <summary>Reconcile the frame's buff churn into genuine gain/loss events. Ticked once per frame
        /// (before <see cref="EventDispatcher.Tick"/>, so the reconciled events flush this frame).</summary>
        public static void Tick() => _instance?.Reconcile();

        // The game's own "don't narrate this" contract, checked AT CAPTURE TIME (our handlers run
        // synchronously inside rule application, where these flags are live): the scoped
        // GameLogDisabled context (cutscene spell executions, scripted DealDamage/kills wrap
        // themselves in it) plus the per-rule DisableBattleLog (cutscene attacks flag the rule; it
        // also folds in the initiator unit's own flag). The game's combat log gates on exactly these —
        // it's why cutscenes are silent there while our event speech used to read the whole fight.
        private static bool LogSuppressed(RulebookEvent rule = null)
            => (bool)ContextData<GameLogDisabled>.Current || (rule != null && rule.DisableBattleLog);

        public void HandleDamageDealt(RuleDealDamage dealDamage)
        {
            if (dealDamage?.Target != null && dealDamage.Result > 0 && !LogSuppressed(dealDamage))
                EventDispatcher.Raise(new DamageEvent(dealDamage.Target, dealDamage.Result));
        }

        // IHealingHandler — fires per heal instance, after the rule applies. Value is the actual HP restored
        // (already clamped to missing health); gate on > 0 like the game's own combat text / overtips do, so
        // a no-op heal (target at full, IsFake, interrupted) stays silent.
        public void HandleHealing(RuleHealDamage healDamage)
        {
            if (healDamage?.Target != null && healDamage.Value > 0 && !LogSuppressed(healDamage))
                EventDispatcher.Raise(new HealEvent(healDamage.Target, healDamage.Value));
        }

        // IUnitHandler — death fires once per unit; the other members are no-ops we just have to carry.
        // (No rule here; the ambient flag covers scripted kills — UnitLifeController wraps them.)
        public void HandleUnitDeath(UnitEntityData unit)
        {
            if (unit != null && !LogSuppressed()) EventDispatcher.Raise(new UnitDeathEvent(unit));
        }
        public void HandleUnitDestroyed(UnitEntityData unit) { }
        public void HandleUnitSpawned(UnitEntityData unit) { }

        // IGlobalRulebookHandler<RuleDealStatDamage> — ability score damage/drain, after the rule resolves.
        // Must be the *Global* variant: it carries IGlobalRulebookSubscriber, which is what
        // RulebookEventBus.Subscribe actually registers — bare IRulebookHandler<T> would never fire.
        public void OnEventAboutToTrigger(RuleDealStatDamage evt) { }
        public void OnEventDidTrigger(RuleDealStatDamage evt)
        {
            if (evt?.Target != null && !evt.Immune && evt.Result > 0 && !LogSuppressed(evt))
                EventDispatcher.Raise(new StatDamageEvent(evt.Target, evt.Stat.Type, evt.Result, evt.IsDrain));
        }

        // The suppression check must live HERE (capture time), not in Reconcile — the scoped flag is
        // only on the stack while the cutscene ability is executing; Reconcile runs a frame later.
        public void HandleBuffDidAdded(Buff buff)
        {
            if (LogSuppressed()) return;
            var key = KeyOf(buff);
            if (key != null) _frameAdds[key.Value] = buff;
        }

        public void HandleBuffDidRemoved(Buff buff)
        {
            if (LogSuppressed()) return;
            var key = KeyOf(buff);
            if (key != null) _frameRemoves[key.Value] = buff;
        }

        private void Reconcile()
        {
            if (_frameAdds.Count == 0 && _frameRemoves.Count == 0) return;

            // While the player has no (settled) control — cutscene, dialogue, rest — buff churn
            // updates the active set SILENTLY: cutscene beats holster/swap weapons and re-materialize
            // units, detaching and re-attaching restriction-bound activatable buffs (the repeated
            // "gained/lost Power Attack" repro), state the player can't act on anyway. Tracking it
            // mutely (rather than dropping it at capture) keeps _active accurate, so the first change
            // AFTER control returns is judged against true state. Settled, not raw: the add half of a
            // churn pair can land on a between-beat controller blip.
            bool silent = !ControlState.Settled;

            // Gains: added this frame and not already active (HashSet.Add is false for a dup/refresh).
            foreach (var kv in _frameAdds)
                if (_active.Add(kv.Key) && !silent)
                    EventDispatcher.Raise(new BuffGainedEvent(kv.Value));

            // Losses: removed this frame, was active, and NOT re-added this frame (a re-add = refresh).
            foreach (var kv in _frameRemoves)
                if (!_frameAdds.ContainsKey(kv.Key) && _active.Remove(kv.Key) && !silent)
                    EventDispatcher.Raise(new BuffLostEvent(kv.Value));

            _frameAdds.Clear();
            _frameRemoves.Clear();
        }

        // (unit, blueprint) identity, or null to ignore the buff (no owner, or hidden/empty per the
        // game's own combat-log filter).
        private static BuffKey? KeyOf(Buff buff)
        {
            var unit = buff?.Owner;
            var bp = buff?.Blueprint;
            if (unit == null || bp == null) return null;
            if (bp.IsHiddenInUI || string.IsNullOrEmpty(buff.Name)) return null;
            return new BuffKey(unit, bp);
        }

        private struct BuffKey : IEquatable<BuffKey>
        {
            private readonly UnitEntityData _unit;
            private readonly object _bp;
            public BuffKey(UnitEntityData unit, object bp) { _unit = unit; _bp = bp; }
            public bool Equals(BuffKey o) => ReferenceEquals(_unit, o._unit) && ReferenceEquals(_bp, o._bp);
            public override bool Equals(object o) => o is BuffKey k && Equals(k);
            public override int GetHashCode()
                => ((_unit?.GetHashCode() ?? 0) * 397) ^ (_bp?.GetHashCode() ?? 0);
        }
    }
}
