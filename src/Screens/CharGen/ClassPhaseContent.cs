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

            if (_detailed) FillMechanic();
            else FillShort();
        }

        // Short mode: render the game's own selected template — the one it placed in
        // ReactiveTooltipTemplate and feeds to the InfoSectionView — at type=Info, so we match the
        // screen exactly (short build-info for a class with a short desc; first-level-class with full
        // description + skills + features for archetypes/short-less classes; level templates in
        // level-up mode). No field-guessing.
        private void FillShort()
        {
            // One flat list, not a list-per-section: the game renders these bricks as a single
            // vertical stack anyway (level headers are just text lines, not separate groups). As one
            // ListContainer it's a single Tab-stop that Down-arrow walks straight through — instead
            // of ~20 per-level lists you'd have to Tab past to reach the Next button. (Build = flat;
            // BuildSections would split at every "Level N" title.)
            var tpl = Phase.ReactiveTooltipTemplate.Value;
            var list = new ListContainer();
            foreach (var el in TooltipReader.Build(tpl, expanded: true, type: TooltipTemplateType.Info))
                list.Add(el);
            if (list.Children.Count > 0) _detailPanel.Add(list);
        }

        // Mechanic mode: the plain bindings the game's mechanic view shows — name + full description
        // (LocalizedDescription, archetype-aware) + the stat blocks below. (Progression grid TODO.)
        private void FillMechanic()
        {
            var head = new ListContainer(null);
            var name = Phase.ClassDisplayName.Value;
            var desc = Phase.ClassDescription.Value;
            if (!string.IsNullOrEmpty(name)) head.Add(new TextElement(name, "heading"));
            if (!string.IsNullOrEmpty(desc)) head.Add(new TextElement(desc));
            _detailPanel.Add(head);

            // Mechanical stats (saves/BAB/HP) — separate from the build-info template.
            var m = Phase.MartialStatsVM.Value;
            if (m != null)
            {
                var stats = new ListContainer("Statistics");
                stats.Add(new TextElement("Base attack bonus: " + m.BAB.Value));
                stats.Add(new TextElement("Fortitude save: " + m.Fortitude.Value));
                stats.Add(new TextElement("Reflex save: " + m.Reflex.Value));
                stats.Add(new TextElement("Will save: " + m.Will.Value));
                stats.Add(new TextElement("Hit points at first level: " + m.HitPointsFirstLevel.Value));
                stats.Add(new TextElement("Hit points per level: " + m.HitPointsPerLevel.Value));
                _detailPanel.Add(stats);
            }

            // Spellcasting (only for casters).
            var c = Phase.ClassCasterStatsVM.Value;
            if (c != null && c.CanCast.Value)
            {
                var caster = new ListContainer("Spellcasting");
                caster.Add(new TextElement("Maximum spell level: " + c.MaxSpellsLevel.Value));
                caster.Add(new TextElement("Casting ability: " + c.CasterAbilityScore.Value));
                caster.Add(new TextElement("Caster type: " + c.CasterMindType.Value));
                caster.Add(new TextElement("Spellbook: " + c.SpellbookUseType.Value));
                _detailPanel.Add(caster);
            }

            // Class skills.
            var s = Phase.ClassSkillsVM.Value;
            if (s != null && s.ClassSkills != null && s.ClassSkills.Count > 0)
            {
                var skills = new ListContainer("Class skills");
                foreach (var entry in s.ClassSkills)
                    if (entry != null) skills.Add(new TextElement(entry.DisplayName));
                _detailPanel.Add(skills);
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
