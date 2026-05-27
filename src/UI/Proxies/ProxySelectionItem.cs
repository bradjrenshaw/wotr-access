using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.SelectionGroup;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A radio choice in any <see cref="SelectionGroupEntityVM"/> group (race, gender, alignment, …).
    /// Activate selects it via the game's own SetSelectedFromView; reads "disabled" when unavailable.
    /// The label (and an optional Space drill-in tooltip) are supplied by the caller, since they live
    /// on the concrete item type, not the shared base. (Class uses <see cref="ProxyClassItem"/>
    /// instead — it also gates on prerequisites.)
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySelectionItem : UIElement
    {
        private readonly SelectionGroupEntityVM _vm;
        private readonly Func<string> _label;
        private readonly Func<TooltipBaseTemplate> _tooltip; // optional Space drill-in, resolved live

        public ProxySelectionItem(SelectionGroupEntityVM vm, Func<string> label, Func<TooltipBaseTemplate> tooltip = null)
        {
            _vm = vm;
            _label = label;
            _tooltip = tooltip;
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _tooltip != null ? _tooltip() : null;

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool Available => _vm != null && _vm.IsAvailable.Value;
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label?.Invoke() ?? ""));
            yield return new RoleAnnouncement("option");
            yield return new SelectedAnnouncement(IsSelected);
            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Available)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"),
                    _ => _vm.SetSelectedFromView(true));
        }
    }
}
