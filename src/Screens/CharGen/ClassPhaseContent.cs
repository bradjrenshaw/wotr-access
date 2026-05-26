using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary
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

        // The Short/Mechanic toggle is VIEW state (CharGenClassPhaseDetailedPCView.m_ViewMode), not on
        // the VM — so to make the rendered screen track our toggle, we reach the live view by type and
        // call its private SwitchMode(). Reflection + a typed FindObjectOfType is the only path (no VM
        // hook); a no-op when the view is in Level-up mode (no switch) or isn't present.
        private static readonly System.Type DetailViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.Class.CharGenClassPhaseDetailedPCView");
        private static readonly System.Reflection.FieldInfo ViewModeField =
            DetailViewType != null ? AccessTools.Field(DetailViewType, "m_ViewMode") : null;
        private static readonly System.Reflection.MethodInfo SwitchModeMethod =
            DetailViewType != null ? AccessTools.Method(DetailViewType, "SwitchMode") : null;
        private static readonly System.Type ViewModeEnum = ResolveModeEnum();

        private static System.Type ResolveModeEnum()
        {
            var t = ViewModeField?.FieldType; // ReactiveProperty<ClassDetailedViewMode?>
            if (t == null || !t.IsGenericType) return null;
            var arg = t.GetGenericArguments()[0];
            return System.Nullable.GetUnderlyingType(arg) ?? arg;
        }

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
                () => { _detailed = !_detailed; SyncGameViewMode(); FillDetail(); }));

            _detailPanel = new Panel("Details");
            content.Add(_detailPanel);

            _classFrom = Phase.SelectedClassVM.Value;
            _archetypeFrom = Phase.SelectedArchetypeVM.Value;
            SyncGameViewMode(); // align the on-screen view with our default on entry
            FillArchetypes();
            FillDetail();
        }

        // Flip the game's own detail view (Short/Mechanic) to match our toggle so the rendered screen
        // tracks what we read. View state, so we set it on the live view; left alone in Level-up mode.
        private void SyncGameViewMode()
        {
            if (ViewModeField == null || SwitchModeMethod == null || ViewModeEnum == null) return;
            var view = UnityEngine.Object.FindObjectOfType(DetailViewType);
            if (view == null) return;
            var rp = ViewModeField.GetValue(view);
            var cur = rp?.GetType().GetProperty("Value")?.GetValue(rp);
            if (cur == null) return;                                            // view not bound yet
            if (cur.Equals(System.Enum.Parse(ViewModeEnum, "Levelup"))) return; // no switch in level-up
            var target = System.Enum.Parse(ViewModeEnum, _detailed ? "MechanicDescription" : "ShortDescription");
            if (!cur.Equals(target)) SwitchModeMethod.Invoke(view, null);       // toggles Short↔Mechanic to match
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

            if (_detailed) FillMechanic();
            else FillShort();
        }

        // Short mode mirrors the game's InfoSectionView: render the exact template it feeds there
        // (ReactiveTooltipTemplate) as one treeview — short build-info for a short-desc class; the
        // first-level-class body (full description + skills + per-level features) for an archetype.
        private void FillShort()
        {
            var tpl = Phase.ReactiveTooltipTemplate.Value;
            if (tpl == null) return;
            var tree = new TreeGroup();
            foreach (var node in TooltipTreeBuilder.Build(tpl, TooltipTemplateType.Info))
                tree.Add(node);
            if (tree.Children.Count == 0) return;
            TooltipTreeBuilder.ExpandStructural(tree); // read fully on focus; drill-ins stay lazy
            _detailPanel.Add(tree);
        }

        // Mechanic mode reconstructs the game's Detailed PANEL (which is plain VM bindings, NOT a
        // tooltip) as a treeview tab-stop: name, full description, then Martial / Caster / Class-skill
        // groups (each value a child). After the tree come the progression grid (TODO) and — only
        // when actually present — the auto-levelup button, as their own tab-stops.
        private void FillMechanic()
        {
            var tree = new TreeGroup();

            var name = Phase.ClassDisplayName.Value;
            if (!string.IsNullOrEmpty(name)) tree.Add(TooltipNode.Leaf(name));
            var desc = Phase.ClassDescription.Value;
            if (!string.IsNullOrEmpty(desc)) tree.Add(TooltipNode.Leaf(desc));

            // Saves/BAB are progression GRADES here (the panel's representation), not numbers.
            var m = Phase.MartialStatsVM.Value;
            if (m != null)
            {
                // Each stat carries the game's glossary tooltip as a (collapsed) drill-in — Right to
                // read what e.g. "Fortitude save" means. Keys match the panel views' SetTooltip calls.
                var g = TooltipNode.Branch("Martial stats");
                g.Add(TooltipNode.Leaf("Base attack bonus: " + m.BAB.Value, drillIn: new TooltipTemplateGlossary("BaseAttackBonus")));
                g.Add(TooltipNode.Leaf("Fortitude: " + m.Fortitude.Value, drillIn: new TooltipTemplateGlossary("SaveFortitude")));
                g.Add(TooltipNode.Leaf("Reflex: " + m.Reflex.Value, drillIn: new TooltipTemplateGlossary("SaveReflex")));
                g.Add(TooltipNode.Leaf("Will: " + m.Will.Value, drillIn: new TooltipTemplateGlossary("SaveWill")));
                g.Add(TooltipNode.Leaf("Hit points at first level: " + m.HitPointsFirstLevel.Value, drillIn: new TooltipTemplateGlossary("HP")));
                g.Add(TooltipNode.Leaf("Hit points per level: " + m.HitPointsPerLevel.Value, drillIn: new TooltipTemplateGlossary("HPPerLevel")));
                tree.Add(g);
            }

            var c = Phase.ClassCasterStatsVM.Value;
            if (c != null && c.CanCast.Value)
            {
                var g = TooltipNode.Branch("Caster stats");
                g.Add(TooltipNode.Leaf("Maximum spell level: " + c.MaxSpellsLevel.Value, drillIn: new TooltipTemplateGlossary("MaxSpellsLevel")));
                g.Add(TooltipNode.Leaf("Casting ability: " + c.CasterAbilityScore.Value, drillIn: new TooltipTemplateGlossary("CasterAbilityScore")));
                g.Add(TooltipNode.Leaf("Caster type: " + c.CasterMindType.Value, drillIn: new TooltipTemplateGlossary("CasterType")));
                g.Add(TooltipNode.Leaf("Spellbook: " + c.SpellbookUseType.Value, drillIn: new TooltipTemplateGlossary("CasterMemoryType")));
                tree.Add(g);
            }

            var s = Phase.ClassSkillsVM.Value;
            if (s != null && s.ClassSkills != null && s.ClassSkills.Count > 0)
            {
                var g = TooltipNode.Branch("Class skills");
                foreach (var entry in s.ClassSkills)
                    if (entry != null) g.Add(TooltipNode.Leaf(entry.DisplayName, drillIn: entry.TooltipTemplate));
                tree.Add(g);
            }

            if (tree.Children.Count > 0)
            {
                TooltipTreeBuilder.ExpandStructural(tree); // groups expanded so it reads fully
                _detailPanel.Add(tree);
            }

            // Progression grid as a Table tab-stop: levels = columns, feature lines = rows (banded by
            // class / Feats / Shared). Space on a cell drills into the feature.
            var grid = ProgressionGrid.Build(Phase.ProgressionVM);
            if (grid != null) _detailPanel.Add(grid);

            // Auto-levelup button: present only when the game shows it active (first level + a default
            // build plan). Label/enabled mirror the view; activate opens its confirm dialog.
            var al = Phase.AutoLevelupButtonVM;
            if (al != null && al.ButtonIsActiveProperty.Value)
            {
                _detailPanel.Add(new ProxyActionButton(
                    () => al.AutoLevelupIsAccessible.Value
                        ? UIStrings.Instance.CharGen.LoadDefaultClassButton
                        : UIStrings.Instance.CharGen.NoDefaultBuildForArchetype,
                    () => al.ButtonIsActiveProperty.Value && al.AutoLevelupIsAccessible.Value && !al.AutoLevelupIsOnProperty.Value,
                    () => al.RequestActivateAutoLevelup()));
            }
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
