using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A voice choice in the chargen Voice phase. Activate mirrors the game's item view: if it's
    /// already the selected voice, replay its sample (PlayPreview); otherwise select it — which the
    /// game plays off the resulting Barks. So a fresh pick plays once via selection, and re-activating
    /// the current voice replays it (no double-play in either case).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyVoiceItem : UIElement
    {
        private readonly CharGenVoiceItemVM _vm;

        public ProxyVoiceItem(CharGenVoiceItemVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true;

        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.DisplayName ?? ""));
            yield return new RoleAnnouncement("option");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_vm == null) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"), _ =>
            {
                if (IsSelected) _vm.Barks?.PlayPreview();   // already chosen → replay the sample
                else _vm.SetSelectedFromView(true);          // choose it → the game plays the sample
            });
        }
    }
}
