using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A class choice in the chargen Class phase. Activate selects it (via the game's own
    /// SetSelectedFromView). Mirrors the game's own item view: every shown class is built with
    /// <c>canSelect: true</c>, so <see cref="CharGenClassSelectorItemVM.IsAvailible"/> (which drives the
    /// button's interactability) is the real clickable gate — a locked prestige class has
    /// <c>PrerequisitesDone == false</c> but is still CLICKABLE (the game's "Forbidden" layer), so you can
    /// select it to view it / read its requirements. We mark that state with "prerequisites not met"
    /// rather than "disabled". (Archetypes — the nested sub-options — come in a follow-up.)
    /// </summary>
    public sealed class ProxyClassItem : UIElement
    {
        // Shares the "radio button" settings category + announcement order (see ProxySelectionItem).
        public override System.Type AnnouncementOrderType => typeof(ProxySelectionItem);

        private readonly CharGenClassSelectorItemVM _vm;

        public ProxyClassItem(CharGenClassSelectorItemVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        // Interactable == the game's m_Button.SetInteractable(IsAvailable): clickable even when prerequisites
        // aren't met. PrereqsMet is the separate "Forbidden" condition (red in the game).
        private bool Interactable => _vm != null && _vm.IsAvailible;
        private bool PrereqsMet => _vm != null && _vm.PrerequisitesDone;
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.DisplayName ?? ""));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(IsSelected);
            // Locked prestige class: still selectable to view, but its prerequisites aren't met — say so
            // (the tooltip drill-in carries the specifics). Don't conflate this with "disabled".
            if (Interactable && !PrereqsMet)
                yield return new ValueAnnouncement(Message.Localized("ui", "state.prerequisites_not_met"));
            yield return new EnabledAnnouncement(Interactable);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Clickable whenever the game's button is interactable, regardless of prerequisites. Mirror the
            // item view's OnClick EXACTLY: route through WarnLevelupPlansWillDropBeforeAction first (in
            // level-up mode, if this change would discard a followed auto-levelup plan, it opens the game's
            // confirm dialog — our MessageModalScreen makes that accessible — and only proceeds on Yes; in
            // chargen with no plan it runs straight through), then ApplyOnClick: TryUnselectArchetypes first
            // (clicking the selected class while an archetype is chosen, or the selected archetype itself,
            // drops back to the base class), otherwise toggle selection (deselect no-ops when the group
            // forbids switch-off, as the top-level class list does).
            if (Interactable)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                    _ => _vm.WarnLevelupPlansWillDropBeforeAction(
                        () => { if (!_vm.TryUnselectArchetypes()) _vm.SetSelectedFromView(!_vm.IsSelected.Value); }));
        }
    }
}
