using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Class phase: the class list, the selected class's archetypes (nested sub-options), and a live
    /// Details panel that mirrors the game's two detail modes (toggled by a "Detailed description"
    /// switch, matching its in-UI button):
    ///   • Short — renders the exact template the game put in <c>ReactiveTooltipTemplate</c> (the
    ///     InfoSectionView's source), at <see cref="TooltipTemplateType.Info"/>. The game chooses
    ///     that template per item (short-description build info for a class that has one; the
    ///     first-level-class template — full description + skills + features — for archetypes and
    ///     short-less classes), so we don't second-guess which field to read.
    ///   • Mechanic — the plain bindings: name + full description + saves/BAB/HP grades + caster
    ///     stats + class skills. (The progression grid is still TODO.)
    /// Classes/archetypes come from the canonical (selectable) instances so SetSelectedFromView
    /// works. The archetype list refreshes on class change; the detail on class or archetype change.
    /// </summary>
    public sealed class ClassPhaseContent : CharGenPhaseContent<CharGenClassPhaseVM>
    {
        // Private list of class items — reflected (no public accessor; reflection is fine here).
        private static readonly System.Reflection.FieldInfo ClassesField =
            AccessTools.Field(typeof(CharGenClassPhaseVM), "m_ClassesVMs");

        private Panel _archetypePanel;
        private Panel _detailPanel;
        private object _classFrom;
        private object _archetypeFrom;
        private bool _detailed; // false = Short mode (the chargen default), true = Mechanic mode

        public ClassPhaseContent(CharGenClassPhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            var classList = new ListContainer();
            foreach (var item in Classes())
                classList.Add(new ProxyClassItem(item));
            content.Add(classList);

            _archetypePanel = new Panel();
            content.Add(_archetypePanel);

            // Mode switch — mirrors the game's "Detailed description" button. Flipping it re-renders
            // the detail panel in place (a sibling, so the toggle keeps focus across the rebuild).
            content.Add(new ProxyBoolToggle(
                UIStrings.Instance.CharGen.DetailedDescription,
                () => _detailed,
                () => { _detailed = !_detailed; FillDetail(); }));

            _detailPanel = new Panel("Details");
            content.Add(_detailPanel);

            _classFrom = Phase.SelectedClassVM.Value;
            _archetypeFrom = Phase.SelectedArchetypeVM.Value;
            FillArchetypes();
            FillDetail();
        }

        public override void Tick()
        {
            var cls = Phase.SelectedClassVM.Value;
            var arch = Phase.SelectedArchetypeVM.Value;
            if (!ReferenceEquals(cls, _classFrom))
            {
                _classFrom = cls;
                _archetypeFrom = arch;
                FillArchetypes(); // new class → its archetypes
                FillDetail();
            }
            else if (!ReferenceEquals(arch, _archetypeFrom))
            {
                _archetypeFrom = arch;
                FillDetail(); // archetype changes the stats/detail
            }
        }

        private void FillArchetypes()
        {
            if (_archetypePanel == null) return;
            _archetypePanel.Clear();
            var archetypes = Archetypes().ToList();
            if (archetypes.Count == 0) return; // class has no archetypes → no list

            var list = new ListContainer("Archetypes");
            foreach (var a in archetypes) list.Add(new ProxyClassItem(a));
            _archetypePanel.Add(list);
        }

        private void FillDetail()
        {
            if (_detailPanel == null) return;
            _detailPanel.Clear();
            // Bail when nothing's selected yet (a class auto-selects on phase entry, so this is rare).
            if (Phase.SelectedClassVM.Value == null) return;

            // Both modes are a single treeview from a game tooltip template (via TooltipTreeBuilder):
            //  • Short    — the template the game itself feeds its InfoSectionView (ReactiveTooltipTemplate):
            //               short build-info for a short-desc class; the first-level-class body (full
            //               description + skills + per-level features) for an archetype/short-less class.
            //  • Mechanic — the class's mechanic tooltip: name + prerequisites + description + signature
            //               features + casting/memory + saves/BAB + spell table + skills (archetype-aware).
            // The full per-level progression grid (UnitProgressionVM) is still TODO in both.
            var subject = Phase.SelectedArchetypeVM.Value ?? Phase.SelectedClassVM.Value;
            var tpl = _detailed
                ? subject?.TooltipTemplateMechanicClassDescription
                : Phase.ReactiveTooltipTemplate.Value;
            FillTree(tpl);
        }

        private void FillTree(Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate tpl)
        {
            if (tpl == null) return;
            var tree = new TreeGroup();
            foreach (var node in TooltipTreeBuilder.Build(tpl, TooltipTemplateType.Info))
                tree.Add(node);
            if (tree.Children.Count == 0) return;
            TooltipTreeBuilder.ExpandStructural(tree); // read fully on focus; drill-ins stay lazy
            _detailPanel.Add(tree);
        }

        private IEnumerable<CharGenClassSelectorItemVM> Classes()
        {
            var list = ClassesField?.GetValue(Phase) as IEnumerable<CharGenClassSelectorItemVM>;
            if (list == null) yield break;
            foreach (var c in list)
                if (c != null) yield return c;
        }

        // The selected class's archetypes — cached in the nested group once the class is selected
        // (expanded), so these are the same instances SetSelectedFromView acts on.
        private IEnumerable<CharGenClassSelectorItemVM> Archetypes()
        {
            var cls = Phase.SelectedClassVM.Value;
            if (cls == null || Phase.ClassSelector == null) yield break;
            if (Phase.ClassSelector.NestedEntityCollections.TryGetValue(cls, out var list) && list != null)
                foreach (var e in list)
                    if (e is CharGenClassSelectorItemVM a) yield return a;
        }
    }
}
