using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A class choice in the chargen Class phase. Activate selects it (via the game's own
    /// SetSelectedFromView); reads "disabled" when unavailable or forbidden by prerequisites.
    /// (Archetypes — the nested sub-options — come in a follow-up.)
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyClassItem : UIElement
    {
        private readonly CharGenClassSelectorItemVM _vm;

        public ProxyClassItem(CharGenClassSelectorItemVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool Available => _vm != null && _vm.IsAvailible && _vm.PrerequisitesDone;
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.DisplayName ?? ""));
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
