using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.SelectionGroup;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A radio choice in any <see cref="SelectionGroupEntityVM"/> group (race, gender, …). Activate
    /// selects it via the game's own SetSelectedFromView; reads "disabled" when unavailable. The label
    /// is supplied by the caller since it lives on the concrete item type, not the shared base.
    /// (Class uses <see cref="ProxyClassItem"/> instead — it also gates on prerequisites.)
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySelectionItem : UIElement
    {
        private readonly SelectionGroupEntityVM _vm;
        private readonly Func<string> _label;

        public ProxySelectionItem(SelectionGroupEntityVM vm, Func<string> label)
        {
            _vm = vm;
            _label = label;
        }

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
