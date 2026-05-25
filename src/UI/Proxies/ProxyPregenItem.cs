using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A premade ("pregen") character in the first chargen phase — reads name, race, class, and
    /// role. Activate selects it (applies the whole build); once selected, the tooltip key reads
    /// its full build details (the phase's InfoVM template, via the tooltip reader).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyPregenItem : UIElement
    {
        private readonly CharGenPregenSelectorItemVM _vm;
        private readonly CharGenPregenPhaseVM _phase;

        public ProxyPregenItem(CharGenPregenSelectorItemVM vm, CharGenPregenPhaseVM phase)
        {
            _vm = vm;
            _phase = phase;
        }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        private string Describe()
        {
            if (_vm == null) return "";
            var parts = new[] { _vm.CharacterName.Value, _vm.Race.Value, _vm.Class.Value, _vm.Role.Value };
            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Describe()));
            yield return new RoleAnnouncement("option");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"),
                _ => _vm?.SetSelectedFromView(true));
        }

        // Details (class description + features) are the selected character's, shown by the phase.
        public override TooltipBaseTemplate GetTooltipTemplate()
            => IsSelected && _phase?.InfoVM != null ? _phase.InfoVM.CurrentTooltip : null;
    }
}
