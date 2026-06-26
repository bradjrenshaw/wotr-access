using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A synthetic "No archetype" radio at the top of the chargen archetype list — the explicit "base
    /// class, no archetype" option. Reads selected when the current class has no archetype chosen
    /// (<c>SelectedArchetypeVM == null</c>); activating it clears any chosen archetype via the game's
    /// <see cref="CharGenClassSelectorItemVM.TryUnselectArchetypes"/> on the selected class, returning to
    /// the base class. The game has no VM for this — it expects you to re-click the class or the selected
    /// archetype — so we synthesize a clear, discoverable control alongside those gestures.
    /// </summary>
    public sealed class ProxyNoArchetypeItem : UIElement
    {
        // Shares the "radio button" settings category + announcement order (see ProxySelectionItem).
        public override System.Type AnnouncementOrderType => typeof(ProxySelectionItem);

        private readonly CharGenClassPhaseVM _phase;

        public ProxyNoArchetypeItem(CharGenClassPhaseVM phase) { _phase = phase; }

        public override bool ReannounceOnActivate => true; // clearing flips it to "selected" in place

        private bool IsSelected => _phase != null && _phase.SelectedArchetypeVM.Value == null;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "chargen.no_archetype"));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(IsSelected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Clear the archetype (no-op when none is chosen — we're already on the base class). Like the
            // class/archetype clicks, route through WarnLevelupPlansWillDropBeforeAction so dropping a
            // followed auto-levelup plan prompts the game's confirm dialog first.
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ =>
                {
                    var cls = _phase?.SelectedClassVM.Value;
                    cls?.WarnLevelupPlansWillDropBeforeAction(() => cls.TryUnselectArchetypes());
                });
        }
    }
}
