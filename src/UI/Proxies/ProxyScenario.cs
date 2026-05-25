using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.NewGame.Story;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A campaign/scenario choice in the New Game Story phase (radio option). Activate selects
    /// it via the same SetSelectedFromView the view uses; "selected" marks the current one.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyScenario : UIElement
    {
        private readonly NewGamePhaseStoryScenarioEntityVM _vm;

        public ProxyScenario(NewGamePhaseStoryScenarioEntityVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("option");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"),
                _ => _vm?.SetSelectedFromView(true));
        }
    }
}
