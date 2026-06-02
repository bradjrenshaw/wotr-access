using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The "Custom Character" entry in the pregen list — selecting it (instead of a premade) branches
    /// into the full custom chargen flow. Sits in the premade list as a final option.
    /// </summary>
    public sealed class ProxyCustomCharacter : UIElement
    {
        // A radio peer in the pregen list — share the "radio button" settings category + order.
        public override System.Type AnnouncementOrderType => typeof(ProxySelectionItem);

        private readonly CharGenPregenPhaseVM _phase;

        public ProxyCustomCharacter(CharGenPregenPhaseVM phase) { _phase = phase; }

        public override bool ReannounceOnActivate => true;

        private bool IsSelected => _phase != null && _phase.IsCustomCharacter.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "label.custom_character"));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ => _phase?.SelectCreateCustomCharacter());
        }
    }
}
