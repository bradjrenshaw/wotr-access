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
using Kingmaker.EntitySystem;             // EntityDataBase (IInGameHandler)

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
        : IDamageHandler, IHealingHandler, IUnitBuffHandler, IUnitHandler, IGlobalRulebookHandler<RuleDealStatDamage>,
          IInGameHandler
    {
        // A scene object SHOWN or HIDDEN by script (HideMapObject — a puzzle's progress runes, a
        // revealed prop): the sighted player watches it appear/vanish; by ear this is the only
        // feedback (the Shield Maze torture-room combination lock). The game raises this from the
        // IsInGame setter; a hidden object leaves the entity pools entirely, so the world model's
        // own diff can't tell a scripted hide from an area change — this is the signal. Filters:
        // map objects with something to SEE (a renderer — script zones, spawners, trap zones have
        // none; units have their own events), never mid-cutscene/dialogue (dressing churns, and
        // control is lost anyway), never while an area loads, within earshot of the party, and not
        // under fog. Announced through the ordinary event pipeline (Events settings).
        private const float ShownEventRadius = 20f;
        public void HandleObjectInGameChanged(EntityDataBase entity)
        {
            try
            {
                var mo = entity as MapObjectEntityData;
                if (mo == null || mo is Kingmaker.View.MapObjects.Traps.TrapObjectData) return;
                var game = Kingmaker.Game.Instance;
                if (game == null || game.CurrentlyLoadedArea == null || game.CutsceneLock.Active) return;
                if (Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance.IsLoadingInProcess) return;
                var mode = game.CurrentMode;
                if (mode == Kingmaker.GameModes.GameModeType.Cutscene || mode == Kingmaker.GameModes.GameModeType.Dialog) return;
                var view = mo.View;
                if (view == null || view.GetComponentInChildren<UnityEngine.Renderer>(true) == null) return;
                var pos = view.transform.position;
                if (WrathAccess.Exploration.Geo.Distance(WrathAccess.Exploration.Overlays.Cursor.PlayerPosition, pos) > ShownEventRadius) return;
                if (Kingmaker.Controllers.FogOfWarController.IsInFogOfWar(pos)) return;
                _visibility.Add(new VisibilityChange(new WrathAccess.Exploration.ProxyMapObject(mo), mo.IsInGame));
                _visibilityLast = UnityEngine.Time.realtimeSinceStartup;
            }
            catch (Exception e) { Main.Log?.Warning("[shown] " + e.Message); }
        }

        // Shown/hidden changes wait out a short quiet window before speaking, so a script that flips
        // several objects together (a failed combination hides every lit rune in one frame) reads as
        // ONE line — the curated group's "all runes stop glowing" (ObjectNames) — rather than four
        // overlapping utterances. Objects with no group, or a group without an "all" line, still
        // speak one by one; a lone change speaks its own curated line ("slot 1: yellow rune glows")
        // or the generic "{name} appears". Real time, not game time: the puzzle plays paused too.
        private struct VisibilityChange
        {
            public readonly WrathAccess.Exploration.ProxyMapObject Item;
            public readonly bool Shown;
            public VisibilityChange(WrathAccess.Exploration.ProxyMapObject item, bool shown) { Item = item; Shown = shown; }
        }
        private const float VisibilityWindow = 0.25f; // seconds of quiet before the batch speaks
        private readonly List<VisibilityChange> _visibility = new List<VisibilityChange>();
        private float _visibilityLast;

        private void FlushVisibility()
        {
            if (_visibility.Count == 0) return;
            if (UnityEngine.Time.realtimeSinceStartup - _visibilityLast < VisibilityWindow) return;
            try
            {
                // Count members per (group, shown) so a group changing en masse collapses to its "all" line.
                var groupCounts = new Dictionary<string, int>();
                foreach (var c in _visibility)
                {
                    var g = c.Item.Naming?.Group;
                    if (string.IsNullOrEmpty(g)) continue;
                    var k = (c.Shown ? "+" : "-") + g;
                    groupCounts.TryGetValue(k, out var n);
                    groupCounts[k] = n + 1;
                }
                var spokenGroups = new HashSet<string>();
                foreach (var c in _visibility)
                {
                    var naming = c.Item.Naming;
                    var g = naming?.Group;
                    Message msg = null;
                    if (!string.IsNullOrEmpty(g) && groupCounts[(c.Shown ? "+" : "-") + g] > 1)
                    {
                        var all = naming.ChangeAll(c.Shown);
                        if (all != null)
                        {
                            if (!spokenGroups.Add((c.Shown ? "+" : "-") + g)) continue; // the group already spoke
                            msg = all;
                        }
                    }
                    if (msg == null) msg = naming?.Change(c.Shown);
                    EventDispatcher.Raise(c.Shown ? (ModEvent)new ObjectShownEvent(c.Item, msg) : new ObjectHiddenEvent(c.Item, msg));
                }
            }
            catch (Exception e) { Main.Log?.Warning("[shown] flush: " + e.Message); }
            _visibility.Clear();
        }

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
        public static void Tick() { _instance?.Reconcile(); _instance?.FlushVisibility(); }

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
