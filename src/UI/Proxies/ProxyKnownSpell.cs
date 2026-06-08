using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.GameModes; // GameModeType
using Kingmaker.PubSubSystem; // EventBus, ISlotWasAddedHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // ISpellbookHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.KnownSpells; // AbilityDataVM
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot*
using Kingmaker.UnitLogic; // UnitDescriptor
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using Owlcat.Runtime.UniRx; // DelayedInvoker
using WrathAccess.Screens; // ChoiceSubmenuScreen
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One known spell (<see cref="AbilityDataVM"/>) — the element of a spellbook spell-table row. Announces
    /// the spell name (domain/mythic/opposition folded in) as a "spell", carries its tooltip, and exposes
    /// "Add to action bar" — mirroring SpellbookVM.TryContext's add: it builds the spell's mechanic slot
    /// (memorized / spontaneous / converted, by the same rules) and registers it in the unit's UISettings,
    /// then raises the refresh event so the bar picks it up. Enter = memorize (the game's double-click —
    /// no-op for spontaneous casters); the secondary key opens the context menu (the game's right-click set),
    /// where "Add to action bar" lives so it isn't a one-press append. Cast + the metamagic/magic-hack
    /// removals join the menu in a later slice; add is suppressed in Kingdom / global-map modes, like the game.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyKnownSpell : UIElement
    {
        private readonly AbilityDataVM _vm;
        private readonly UnitDescriptor _unit;

        public ProxyKnownSpell(AbilityDataVM vm, UnitDescriptor unit) { _vm = vm; _unit = unit; }

        public override bool CanFocus => _vm != null;

        private string Name()
        {
            var name = _vm.DisplayName;
            var flags = new List<string>();
            if (_vm.IsDomain) flags.Add("domain");
            if (_vm.IsMythic) flags.Add("mythic");
            if (_vm.IsOpposite) flags.Add("opposition");
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("spell");
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _vm.Tooltip;

        public override IEnumerable<ElementAction> GetActions()
        {
            // Enter = the game's double-click (memorize into the next free slot of this level; no-op for
            // spontaneous casters). Secondary = the right-click context menu.
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "spellbook.memorize"),
                _ => EventBus.RaiseEvent<ISpellbookHandler>(h => h.TryMemorize(_vm)));
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.menu"), _ => OpenMenu());
        }

        // The spell's action menu — the game's right-click set: Cast (when castable now) + Add to action bar.
        // The metamagic / magic-hack removals are tied to the (deferred) metamagic mixer and only apply to
        // spells that already carry metamagic, so they join here with that slice.
        private void OpenMenu()
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(CanCast, Message.Localized("ui", "spellbook.cast").Resolve(), Cast);
            Add(NotKingdomOrMap, Message.Localized("ui", "spellbook.add_to_bar").Resolve(), AddToActionBar);

            if (labels.Count == 0) { Tts.Speak("No actions", interrupt: true); return; }
            var actions = runs;
            ChoiceSubmenuScreen.Open(_vm.DisplayName, labels, -1, idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        // The spell's mechanic action-bar slot — memorized / spontaneous / converted by the same rules the
        // game's SpellbookVM.TryContext uses. Shared by Cast and Add to action bar.
        private MechanicActionBarSlot BuildSlot()
        {
            var spell = _vm.SpellData;
            if (spell == null || _unit == null) return null;
            return (!spell.Blueprint.IsCantrip && !spell.IsSpontaneous)
                ? ((!spell.IsVariable && !spell.GetConversions().Any())
                    ? (MechanicActionBarSlot)new MechanicActionBarSlotMemorizedSpell(spell.SpellSlot) { Unit = _unit }
                    : new MechanicActionBarSlotSpontaneusConvertedSpell { Spell = spell, Unit = _unit })
                : new MechanicActionBarSlotSpontaneousSpell(spell) { Unit = _unit };
        }

        private static bool NotKingdomOrMap =>
            !Game.Instance.IsModeActive(GameModeType.Kingdom) && !Game.Instance.IsModeActive(GameModeType.GlobalMap);

        // Castable right now: a free/available slot for it, the spell available, and not Kingdom/global-map.
        private bool CanCast
        {
            get
            {
                if (!NotKingdomOrMap || _vm.SpellData == null || !_vm.SpellData.IsAvailable) return false;
                var slot = BuildSlot();
                return slot != null && slot.IsPossibleActive();
            }
        }

        // Mirror SpellbookVM.ContextCastAbility: close the window, then (next frame) click the slot — which
        // sets up the cast / world-cursor targeting, exactly like clicking the spell on the action bar.
        private void Cast()
        {
            var slot = BuildSlot();
            if (slot == null) return;
            EventBus.RaiseEvent<INewServiceWindowUIHandler>(h => h.HandleCloseAll());
            DelayedInvoker.InvokeInFrames(slot.OnClick, 1); // after the window closes, like the game's DelayedCast
        }

        private void AddToActionBar()
        {
            var slot = BuildSlot();
            if (slot == null) return;
            _unit.UISettings.SetSlotInternal(slot);
            EventBus.RaiseEvent<ISlotWasAddedHandler>(h => h.SlotWasAdded(_unit));
            Tts.Speak(Message.Localized("ui", "spellbook.added_to_bar").Resolve(), interrupt: false);
        }
    }
}
