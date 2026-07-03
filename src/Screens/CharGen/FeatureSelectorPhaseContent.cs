using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using Kingmaker.UI.MVVM._VM.Other; // RecommendationType
using Kingmaker.UnitLogic.Class.LevelUp; // FeatureSelectionViewState.SelectState
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The generic feature-selector phase (Background, deity, heritage, and other feature picks). The
    /// game's model is "selecting a feature reveals its sub-choices" — so we reflect exactly that with
    /// ZERO view state: each selectable feature is a radio item, and a SELECTED feature that opens
    /// sub-choices reveals them inline as items beneath it (under the feature's name as context), read by
    /// arrowing down. Selecting a sibling collapses the previous (the game's radio), which just falls out
    /// of the live render. Each item reads its name + selected/disabled state (with reason), activates
    /// via the game's SetSelectedFromView, and Space opens its full write-up. The phase name (from the
    /// wizard shell) supplies the header. Built lazily by the game — renders once it materializes.
    ///
    /// (The game's tag-filter dropdown is view-only — no VM state to reflect — so it's dropped here;
    /// type-ahead finds a feature by name on the full list.)
    /// </summary>
    public sealed class FeatureSelectorPhaseContent : CharGenPhaseContent<CharGenFeatureSelectorPhaseVM>
    {
        public FeatureSelectorPhaseContent(CharGenFeatureSelectorPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            // Source + description: the source of this pick — the granting class / race / progression —
            // then the selection's own description ("what this choice is"). The source isn't shown
            // anywhere in-game, but it's too useful to omit (deliberate exception to surface-only-visible).
            var source = SourceLabel();
            if (!string.IsNullOrWhiteSpace(source))
                b.AddItem(ControlId.Structural(k + "source"),
                    GraphNodes.Text(() => Loc.T("chargen.source", new { value = source })));
            var overview = Phase.FeatureSelectorStateVM?.Feature?.Description;
            if (!string.IsNullOrWhiteSpace(overview))
                b.AddItem(ControlId.Structural(k + "overview"), GraphNodes.Text(() => overview));

            if (Phase.SelectionIsProhibited != null && Phase.SelectionIsProhibited.Value)
                b.AddItem(ControlId.Structural(k + "prohibited"),
                    GraphNodes.Text(() => Loc.T("chargen.nothing_to_select")));

            var top = TopEntities();
            if (top.Count == 0) return; // lazy — renders once the selector materializes
            top.Sort(CompareItems); // the game's order (selectable → recommended → name)

            b.BeginStop("features");
            foreach (var it in top) EmitFeature(b, it, k + "f:");
            // (the wizard shell's phase-name context announces "Background" / "Feat")
        }

        // One feature as a radio item. When it's selected AND opens sub-choices, its children are
        // revealed beneath it under its name as context (the game's "select = reveal"); selecting a
        // sibling deselects this one, so its children simply vanish from the next render.
        private void EmitFeature(GraphBuilder b, CharGenFeatureSelectorItemVM vm, string prefix)
        {
            Func<bool> canSelect = () => vm.SelectState == FeatureSelectionViewState.SelectState.CanSelect;
            Func<bool> isSelected = () => vm.IsSelected.Value;
            string key = prefix + vm.GetHashCode();

            b.AddItem(ControlId.Referenced(vm, key), new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => vm.FeatureName ?? ""),
                    GraphNodes.SelectedPart(isSelected),
                    // The unavailable reason, when it can't be picked (live).
                    new NodeAnnouncement(() => !canSelect() && !isSelected()
                        ? vm.NotAvailableLabel?.Value : null, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(() => canSelect() || isSelected()),
                },
                SearchText = () => vm.FeatureName ?? "",
                StateText = () => isSelected() ? Loc.T("state.selected") : null,
                OnActivate = () =>
                {
                    if (!canSelect() && !isSelected()) return;
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    vm.SetSelectedFromView(true); // selecting reveals children next render; siblings drop
                },
                OnTooltip = () =>
                {
                    var tpl = vm.TooltipTemplate();
                    if (tpl != null) TooltipScreen.Open(tpl);
                },
            });

            // Selected + has sub-choices: reveal them inline under this feature's name.
            if (vm.HasNesting && isSelected())
            {
                var kids = Children(vm);
                if (kids.Count > 0)
                {
                    kids.Sort(CompareItems);
                    b.PushContext(vm.FeatureName ?? "");
                    foreach (var kid in kids) EmitFeature(b, kid, key + "/");
                    b.PopContext();
                }
            }
        }

        private List<CharGenFeatureSelectorItemVM> Children(CharGenFeatureSelectorItemVM vm)
        {
            var result = new List<CharGenFeatureSelectorItemVM>();
            var sel = Phase.SelectorVM;
            if (sel != null && sel.NestedEntityCollections.TryGetValue(vm, out var kids) && kids != null)
                foreach (var kk in kids)
                    if (kk is CharGenFeatureSelectorItemVM it) result.Add(it);
            return result;
        }

        // The game's feature-list order (CharGenFeatureSelectorPCView.EntityComparer): selectable
        // (selected or can-select) first, then by recommendation (Recommended > Neutral >
        // NotRecommended), then alphabetical. The game's "already has" tier is a no-op (it compares an
        // item to itself — a copy-paste bug), so we skip it to match the observable order.
        private static int CompareItems(CharGenFeatureSelectorItemVM a, CharGenFeatureSelectorItemVM b)
        {
            int s = Pickable(b).CompareTo(Pickable(a));
            if (s != 0) return s;
            int r = Recommend(b).CompareTo(Recommend(a));
            if (r != 0) return r;
            return string.Compare(a.FeatureName, b.FeatureName, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool Pickable(CharGenFeatureSelectorItemVM x)
            => x.IsSelected.Value || x.SelectState == FeatureSelectionViewState.SelectState.CanSelect;

        private static RecommendationType Recommend(CharGenFeatureSelectorItemVM x)
            => x.FeatureRecommendation.Value?.Recommendation.Value ?? RecommendationType.Neutral;

        // The selection's source blueprint reads as a class / race / progression name (the level-up
        // origin of this pick), or null if unknown.
        private string SourceLabel()
        {
            var st = Phase.FeatureSelectorStateVM?.SelectionState;
            if (st == null) return null;
            var bp = st.Source.Blueprint;
            if (bp is BlueprintCharacterClass c) return c.Name;
            if (bp is BlueprintRace r) return r.Name;
            if (bp is BlueprintFeatureBase f) return f.Name; // progression / feature (e.g. bonus-feat source)
            return null;
        }

        // The top-level feature entities, from the game's own nested-selection collection (keyed by the
        // phase, the root source) so we share the instances the game tracks.
        private List<CharGenFeatureSelectorItemVM> TopEntities()
        {
            var result = new List<CharGenFeatureSelectorItemVM>();
            var sel = Phase.SelectorVM;
            if (sel != null && sel.NestedEntityCollections.TryGetValue(Phase, out var top) && top != null)
                foreach (var e in top)
                    if (e is CharGenFeatureSelectorItemVM it) result.Add(it);
            return result;
        }
    }
}
