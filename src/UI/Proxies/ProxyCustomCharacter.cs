using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The "Custom Character" entry in the pregen list — selecting it (instead of a premade) branches
    /// into the full custom chargen flow. Sits in the premade list as a final option.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(SelectedAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyCustomCharacter : UIElement
    {
        private readonly CharGenPregenPhaseVM _phase;

        public ProxyCustomCharacter(CharGenPregenPhaseVM phase) { _phase = phase; }

        public override bool ReannounceOnActivate => true;

        private bool IsSelected => _phase != null && _phase.IsCustomCharacter.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            // TODO: localize if the game exposes a Custom Character string.
            yield return new LabelAnnouncement(Message.Raw("Custom Character"));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"),
                _ => _phase?.SelectCreateCustomCharacter());
        }
    }
}
