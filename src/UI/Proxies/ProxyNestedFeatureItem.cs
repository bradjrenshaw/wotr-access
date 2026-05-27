using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using Kingmaker.UI.MVVM._VM.Other.NestedSelectionGroup;
using Kingmaker.UnitLogic.Class.LevelUp; // FeatureSelectionViewState.SelectState
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A feature choice in a chargen feature-selector phase (background, deity, heritage, …), as a tree
    /// node that is BOTH the selectable option AND the expandable parent of its sub-choices. This
    /// mirrors the game's nested-selection model, where selecting a feature IS what expands it and
    /// reveals its sub-options inline (radio: one open per level).
    ///
    /// Faithful coupling: <see cref="Expand"/> (Right) and Activate (Enter) both select via the game's
    /// SetSelectedFromView, which drives the game's own <see cref="NestedSelectionGroupRadioVM{T}"/> to
    /// populate the child entities — we then mirror those (the authoritative instances, so selecting a
    /// child registers with the game). Selecting collapses the sibling that was open. Left (Collapse)
    /// only flips our view flag, never the game's state, so the pick persists when you close it.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(EnabledAnnouncement), typeof(ValueAnnouncement))]
    public sealed class ProxyNestedFeatureItem : Container
    {
        private readonly CharGenFeatureSelectorItemVM _vm;
        private readonly NestedSelectionGroupRadioVM<CharGenFeatureSelectorItemVM> _selector;

        public ProxyNestedFeatureItem(CharGenFeatureSelectorItemVM vm,
            NestedSelectionGroupRadioVM<CharGenFeatureSelectorItemVM> selector)
            : base(ContainerShape.Tree, vm?.FeatureName)
        {
            _vm = vm;
            _selector = selector;
        }

        private bool CanSelect => _vm != null && _vm.SelectState == FeatureSelectionViewState.SelectState.CanSelect;
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override bool Expandable => _vm != null && _vm.HasNesting && (CanSelect || IsSelected);
        public override bool ReannounceOnActivate => true; // "selected, N options" after picking

        // Expand == select (faithful). Selecting populates the game's nested entities; we mirror them.
        // Only one sibling stays open at a level (radio), so collapse the others here.
        public override void Expand()
        {
            if (_vm == null) return;
            if (!IsSelected) _vm.SetSelectedFromView(true);
            if (Parent != null)
                foreach (var sib in Parent.Children)
                    if (!ReferenceEquals(sib, this) && sib is ProxyNestedFeatureItem other) { other.Collapse(); other.Clear(); }
            RebuildChildren();
            base.Expand();
        }

        private void RebuildChildren()
        {
            Clear();
            if (_selector != null && _vm != null
                && _selector.NestedEntityCollections.TryGetValue(_vm, out var kids) && kids != null)
                foreach (var k in kids)
                    if (k is CharGenFeatureSelectorItemVM it) Add(new ProxyNestedFeatureItem(it, _selector));
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.FeatureName ?? ""));
            yield return new RoleAnnouncement("option");
            yield return new SelectedAnnouncement(IsSelected);
            yield return new EnabledAnnouncement(CanSelect || IsSelected);
            if (!CanSelect && !IsSelected)
            {
                var reason = _vm?.NotAvailableLabel?.Value;
                if (!string.IsNullOrEmpty(reason)) yield return new ValueAnnouncement(Message.Raw(reason));
            }
            else if (Expanded && Children.Count > 0)
                yield return new ValueAnnouncement(Message.Raw(Children.Count + " options")); // sub-choices revealed
            if (Expandable) yield return new RoleAnnouncement(Expanded ? "expanded" : "collapsed");
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (CanSelect || IsSelected)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"), _ => Expand());
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _vm?.TooltipTemplate();
    }
}
