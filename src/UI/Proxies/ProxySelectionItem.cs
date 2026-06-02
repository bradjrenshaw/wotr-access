using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.SelectionGroup;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The shared control for ANY <see cref="SelectionGroupEntityVM"/> (race, gender, alignment, portrait,
    /// pregen, scenario, voice, spell, …) — it encapsulates the game's selection contract (IsSelected /
    /// IsAvailable / SetSelectedFromView) so no call site reimplements it. The caller supplies the label
    /// (and optional drill-in tooltip), which live on the concrete item type. A single-select group is a
    /// <b>radio button</b> (default); pass <c>role</c> "tab" for a tab, or "checkbox" for a multi-select
    /// group (announces checked/unchecked instead of selected). <c>available</c> overrides the default
    /// availability (e.g. class prerequisites); <c>onActivate</c> overrides the default select (e.g. a
    /// multi-select toggle, or replaying a voice sample when already chosen). (Class still uses
    /// <see cref="ProxyClassItem"/> — its VM is a NestedSelectionGroupEntityVM, a different base.)
    /// </summary>
    // Canonical "radio button": ProxyChoiceOption / ProxyClassItem / ProxyCustomCharacter /
    // ProxyNestedFeatureItem share this settings category + announcement order.
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("radio_button")]
    public sealed class ProxySelectionItem : UIElement
    {
        private readonly SelectionGroupEntityVM _vm;
        private readonly Func<string> _label;
        private readonly Func<TooltipBaseTemplate> _tooltip; // optional Space drill-in, resolved live
        private readonly string _role;
        private readonly Func<bool> _available;
        private readonly Action _activate;
        private readonly bool _suppressSound;

        public ProxySelectionItem(SelectionGroupEntityVM vm, Func<string> label,
            Func<TooltipBaseTemplate> tooltip = null, string role = "radio button",
            Func<bool> available = null, Action onActivate = null, bool suppressActivateSound = false)
        {
            _vm = vm;
            _label = label;
            _tooltip = tooltip;
            _role = role;
            _available = available;
            _activate = onActivate;
            _suppressSound = suppressActivateSound;
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _tooltip != null ? _tooltip() : null;
        public override bool ReannounceOnActivate => true; // selecting/toggling flips it in place

        // Portraits already play their own selection sound off the VM path — don't double it.
        public override Kingmaker.UI.UISoundType? ActivateSound => _suppressSound ? (Kingmaker.UI.UISoundType?)null : base.ActivateSound;

        private bool Available => _available != null ? _available() : (_vm != null && _vm.IsAvailable.Value);
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label?.Invoke() ?? ""));
            yield return new RoleAnnouncement(_role);
            if (_role == "checkbox")
                yield return new ValueAnnouncement(Message.Raw(IsSelected ? "checked" : "unchecked"));
            else
                yield return new SelectedAnnouncement(IsSelected);
            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Available) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw(_role == "checkbox" ? "Toggle" : "Select"),
                _ => { if (_activate != null) _activate(); else _vm?.SetSelectedFromView(true); });
        }
    }
}
