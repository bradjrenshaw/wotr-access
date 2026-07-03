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
    /// The generic feature-selector phase (Background, deity, heritage, and other feature picks),
    /// presented as a tree over the game's nested selection: the selectable features are the top-level
    /// nodes, and a feature that opens sub-choices reveals them inline as children when selected
    /// (faithful to the game — selecting IS expanding, so a node's expansion state IS
    /// <c>IsSelected && HasNesting</c>; a view-only collapse set lets Left close a selected feature
    /// without deselecting it, matching the old behavior). Each node reads its name +
    /// selected/disabled state (with reason), activates via the game's SetSelectedFromView, and Space
    /// opens its full write-up. The phase name supplies the header (e.g. "Background"). The list is
    /// built lazily by the game — immediate mode renders it once it materializes.
    /// </summary>
    public sealed class FeatureSelectorPhaseContent : CharGenPhaseContent<CharGenFeatureSelectorPhaseVM>
    {
        private int _tagIndex; // view state: 0 = All; 1.. = a tag
        private readonly HashSet<object> _viewCollapsed = new HashSet<object>(); // Left-collapsed selected nodes

        public FeatureSelectorPhaseContent(CharGenFeatureSelectorPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            // Source + description: the source of this pick — the granting class / race / progression —
            // then the selection's own description ("what this choice is"). The game shows the
            // description in its info panel; the source isn't shown anywhere, but it's too useful to
            // omit (deliberate exception to surface-only-visible).
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

            // The game's tag dropdown: present only when the selection has tagged features. "All" clears.
            List<string> tagOptions = null;
            if (Phase.HasFeatureTags)
            {
                var tags = Phase.CharGenFeatureSearchVM?.LocalizedValues;
                if (tags != null && tags.Count > 0)
                {
                    tagOptions = new List<string> { Loc.T("filter.all") };
                    tagOptions.AddRange(tags);
                    var opts = tagOptions;
                    b.BeginStop("filter").AddItem(ControlId.Structural(k + "filter"),
                        ModSettingNodes.ChoiceDropdown("Filter by tag", opts,
                            () => _tagIndex, i => _tagIndex = i));
                }
            }

            var top = TopEntities();
            if (top.Count == 0) return; // lazy — renders once the selector materializes

            // Filter by the chosen tag (top level only — sub-choices of a matching feat aren't
            // filtered), then sort the game's way (selectable → recommended → name).
            var items = new List<CharGenFeatureSelectorItemVM>();
            foreach (var it in top)
                if (_tagIndex <= 0 || (tagOptions != null && _tagIndex < tagOptions.Count && it.HasText(tagOptions[_tagIndex])))
                    items.Add(it);
            items.Sort(CompareItems);
            if (items.Count == 0) return;

            b.BeginStop("features");
            foreach (var it in items) EmitFeature(b, it, k + "f:");
            // (the wizard shell's phase-name context announces "Background" / "Feat")
        }

        // One feature node — BOTH the selectable option AND the expandable parent of its sub-choices,
        // mirroring the game's nested-selection model (selecting IS what expands it; radio: one open
        // per level, which falls out of expansion == IsSelected).
        private void EmitFeature(GraphBuilder b, CharGenFeatureSelectorItemVM vm, string prefix)
        {
            Func<bool> canSelect = () => vm.SelectState == FeatureSelectionViewState.SelectState.CanSelect;
            Func<bool> isSelected = () => vm.IsSelected.Value;
            string key = prefix + vm.GetHashCode();

            var vt = new NodeVtable
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
                    _viewCollapsed.Remove(vm);
                    vm.SetSelectedFromView(true); // selecting expands next render; siblings auto-collapse
                },
                OnTooltip = () =>
                {
                    var tpl = vm.TooltipTemplate();
                    if (tpl != null) TooltipScreen.Open(tpl);
                },
            };

            if (vm.HasNesting && (canSelect() || isSelected()))
            {
                // Expansion state IS the game's selection (minus a view-only collapse): Right selects
                // (and thus expands); Left collapses the view WITHOUT deselecting (the old behavior —
                // the pick persists when you close it).
                bool expanded = isSelected() && !_viewCollapsed.Contains(vm);
                vt.OnExpand = () =>
                {
                    _viewCollapsed.Remove(vm);
                    if (!vm.IsSelected.Value) vm.SetSelectedFromView(true);
                };
                vt.OnCollapse = () => _viewCollapsed.Add(vm);
                b.BeginGroup(ControlId.Referenced(vm, key), vt, expanded: expanded);
                if (expanded)
                {
                    var kids = Children(vm);
                    kids.Sort(CompareItems);
                    foreach (var kid in kids) EmitFeature(b, kid, key + "/");
                }
                b.EndGroup();
            }
            else
            {
                b.AddItem(ControlId.Referenced(vm, key), vt);
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
