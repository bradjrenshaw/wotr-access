using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>The Default / Custom tab on the portrait phase. Activate switches to it.</summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyPortraitTab : UIElement
    {
        private readonly CharGenPortraitTabVM _vm;

        public ProxyPortraitTab(CharGenPortraitTabVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true;

        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            // TODO: localize (tab has only the CharGenPortraitTab enum: Default / Custom).
            yield return new LabelAnnouncement(Message.Raw(_vm != null ? _vm.Tab.ToString() : ""));
            yield return new RoleAnnouncement("tab");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"),
                _ => _vm?.SetSelectedFromView(true));
        }
    }
}
