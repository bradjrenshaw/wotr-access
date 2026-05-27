using System.Collections.Generic;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The generic feature-selector phase (Background, deity, heritage, and other feature picks),
    /// presented as a treeview over the game's nested selection: the selectable features are the
    /// top-level nodes, and a feature that opens sub-choices reveals them inline as children when
    /// selected (faithful to the game — selecting IS expanding). Each node reads its name +
    /// selected/disabled state (with reason), activates via the game's SetSelectedFromView, and Space
    /// opens its full write-up. The phase name supplies the header (e.g. "Background"). The list is
    /// built lazily by the game (OnBeginDetailedView), so we (re)build the tree once it materializes.
    /// </summary>
    public sealed class FeatureSelectorPhaseContent : CharGenPhaseContent<CharGenFeatureSelectorPhaseVM>
    {
        private Panel _treePanel;
        private int _topCount = -1;

        public FeatureSelectorPhaseContent(CharGenFeatureSelectorPhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            // Source + description as one vertical list (a single tab-stop): the source of this pick —
            // the granting class / race / progression (item 1) — then the selection's own description,
            // "what this choice is" (item 2). The game shows the description in its info panel; the
            // source isn't shown anywhere, but it's too useful to omit. Just the description, not the
            // selection's full tooltip (which would re-list the options below).
            var info = new ListContainer();
            var source = SourceLabel();
            if (!string.IsNullOrWhiteSpace(source)) info.Add(new TextElement("Source: " + source));
            var overview = Phase.FeatureSelectorStateVM?.Feature?.Description;
            if (!string.IsNullOrWhiteSpace(overview)) info.Add(new TextElement(overview));
            if (info.Children.Count > 0) content.Add(info);

            if (Phase.SelectionIsProhibited != null && Phase.SelectionIsProhibited.Value)
                content.Add(new TextElement("Nothing to select here."));
            _treePanel = new Panel();
            content.Add(_treePanel);
            FillTree();
        }

        public override void Tick()
        {
            // The selector + its top-level entities are created lazily on entering detailed view;
            // (re)build when they appear. Per-node state is read live, so no rebuild for that.
            if (TopEntities().Count != _topCount) FillTree();
        }

        private void FillTree()
        {
            if (_treePanel == null) return;
            var top = TopEntities();
            _topCount = top.Count;
            _treePanel.Clear();
            if (top.Count == 0) return;
            var tree = new TreeGroup(); // unlabeled root; the phase name announces "Background"
            foreach (var it in top) tree.Add(new ProxyNestedFeatureItem(it, Phase.SelectorVM));
            _treePanel.Add(tree);
        }

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
