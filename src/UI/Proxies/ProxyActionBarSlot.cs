using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot + subtypes
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One action-bar slot — an ability / spell / activatable item / special action of the selected unit.
    /// Everything is read LIVE from the slot's underlying <see cref="MechanicActionBarSlot"/>, never the
    /// ActionBarSlotVM's cached reactive properties: the game rapidly repopulates these slots as it swaps
    /// the selected character, so a cached read would lag. Toggle abilities (Fight Defensively, Power
    /// Attack, stances…) announce on/off from the live <c>ActivatableAbility.IsOn</c>. Activate invokes the
    /// VM's <c>OnMainClick</c> (cast/use, or the game's variant/convert chooser); Space drills into the
    /// game tooltip. Reads as a "button" (shares that settings/announcement category).
    /// </summary>
    public sealed class ProxyActionBarSlot : UIElement
    {
        public override Type AnnouncementOrderType => typeof(ProxyActionButton); // button-like

        private readonly ActionBarSlotVM _vm;
        public ProxyActionBarSlot(ActionBarSlotVM vm) { _vm = vm; }

        private MechanicActionBarSlot Slot => _vm?.MechanicActionBarSlot;

        // The live "ui" value key for a toggle ability's state, or null if this slot isn't a toggle:
        //   on + waiting for a target -> "value.targeting"  (e.g. Saddle Up: on, but needs a mount target)
        //   on, no targeting          -> "value.on"          (a plain toggle, e.g. Fight Defensively)
        //   off                       -> "value.off"
        // Read live (never the VM cache). The screen watches this while the slot is focused and announces
        // changes — including async settles/reverts — so we DON'T re-announce eagerly on activate (which
        // would catch the optimistic pre-settle value).
        public string ToggleStateKey
        {
            get
            {
                if (!(Slot is MechanicActionBarSlotActivableAbility act) || act.ActivatableAbility == null) return null;
                var a = act.ActivatableAbility;
                if (!a.IsOn) return "value.off";
                return a.IsWaitingForTarget ? "value.targeting" : "value.on";
            }
        }

        public override bool CanFocus
        {
            get { var s = Slot; return s != null && !(s is MechanicActionBarSlotEmpty) && !s.IsBad(); }
        }

        /// <summary>Live "can use it now" — mirrors the game's own enabled/disabled gate exactly
        /// (<c>IsPossibleActive</c>, the same value the game greys the slot by). The screen watches this while
        /// focused so a slot becoming castable/uncastable is announced. We deliberately do NOT invent an
        /// "enabled" state the game doesn't show (e.g. a charge that's only usable in combat reads disabled
        /// out of combat, just as the game greys it).</summary>
        public bool Enabled { get { var s = Slot; return s != null && s.IsPossibleActive(); } }

        public override TooltipBaseTemplate GetTooltipTemplate() => Slot?.GetTooltipTemplate();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            var s = Slot;
            if (s == null) yield break;
            yield return new LabelAnnouncement(Message.Raw(s.GetTitle()));
            yield return new RoleAnnouncement("button");
            var toggleKey = ToggleStateKey;
            if (toggleKey != null)
                yield return new ValueAnnouncement(Message.Localized("ui", toggleKey)); // on / off / targeting
            else
            {
                int res = s.GetResource();
                if (res > 0)
                {
                    // Label the count by the slot's resource kind: ability uses / spell casts / item charges.
                    var unitBase = ResourceUnitBase(s);
                    yield return unitBase != null
                        ? new ValueAnnouncement(Message.Localized("ui", "value.amount",
                            new { count = res, unit = Message.Localized("ui", "unit." + unitBase + (res == 1 ? "" : "s")).Resolve() }))
                        : new ValueAnnouncement(Message.Raw(res.ToString()));
                }
            }
            yield return new EnabledAnnouncement(Enabled); // delegation-aware (mounted charge etc.)
        }

        // The resource-count's unit, by slot kind (singular base; caller adds "s" for plural). Null = bare
        // number (unknown kind). Spell family is the MechanicActionBarSlotSpell base + the global-magic slot.
        private static string ResourceUnitBase(MechanicActionBarSlot s)
        {
            if (s is MechanicActionBarSlotSpell || s is MechanicActionBarSlotGlobalMagicSpell) return "cast";
            if (s is MechanicActionBarSlotItem) return "charge";
            if (s is MechanicActionBarSlotAbility) return "use";
            return null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.activate"), _ => Activate());
        }

        private void Activate()
        {
            var s = Slot;
            // A click does nothing useful when the slot can neither be activated nor (for a toggle)
            // deactivated — mirrors the game's own gate.
            bool blocked = s != null && !s.IsPossibleActive()
                && !(s is MechanicActionBarSlotActivableAbility act && act.IsPossibleDeactivate());
            int warnings = WarningReader.Count;
            WrathAccess.Exploration.Targeting.Activate(_vm); // branches: self-cast / aim / toggle

            // The game raises a warning only for some refusals (e.g. turn-based "not enough actions"); for a
            // plainly-disabled ability it just plays a sound and reports no text. Give a spoken fallback when
            // it stayed silent, so Enter on a greyed slot always says something.
            if (blocked && WarningReader.Count == warnings)
                Tts.Speak(Message.Localized("ui", "action.cant_use").Resolve(), interrupt: true);
        }
    }
}
