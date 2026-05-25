using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A portrait in the portrait phase. Portraits are images with no name, so the caller supplies
    /// a positional label ("Portrait 3"). Activate selects it (applies it to the character).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyPortraitItem : UIElement
    {
        private readonly CharGenPortraitSelectorItemVM _vm;
        private readonly string _label;

        public ProxyPortraitItem(CharGenPortraitSelectorItemVM vm, string label)
        {
            _vm = vm;
            _label = label;
        }

        public override bool ReannounceOnActivate => true;

        // Portraits already play their own selection sound (off the VM path), so don't double it.
        public override Kingmaker.UI.UISoundType? ActivateSound => null;

        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        // No display name on portraits — use the blueprint's asset name (codey but distinguishing),
        // falling back to the positional label.
        private string Name()
        {
            var bp = _vm != null ? _vm.GetBlueprintPortrait() : null;
            var n = bp != null ? bp.name : null;
            return !string.IsNullOrEmpty(n) ? n : _label;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
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
