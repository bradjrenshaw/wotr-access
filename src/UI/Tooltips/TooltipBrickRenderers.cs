using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.ChupaChupses; // ClassArchetypeDifType
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseBrickVM

namespace WrathAccess.UI.Tooltips
{
    // One renderer per game brick VM type. Register new ones in TooltipBrickRegistry.RegisterDefaults.
    // Single-element bricks implement only GetExpandedElements (flat falls back to it). Multi-element
    // bricks override GetFlatElements to condense.

    public sealed class TextBrickRenderer : TooltipBrickRenderer<TooltipBrickTextVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickTextVM vm) => One(vm?.Text);

        // In the tree, split a multi-line text brick (e.g. the class-skills list, newline-separated)
        // into one leaf per line instead of a single flattened run.
        public override IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm) => Lines((vm as TooltipBrickTextVM)?.Text);
    }

    public sealed class ColorizedTextBrickRenderer : TooltipBrickRenderer<TooltipBrickColorizedTextVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickColorizedTextVM vm) => One(vm?.Text);
    }

    public sealed class TitleBrickRenderer : TooltipBrickRenderer<TooltipBrickTitleVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickTitleVM vm) => One(vm?.Title);
    }

    public sealed class DoubleTextBrickRenderer : TooltipBrickRenderer<TooltipBrickDoubleTextVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickDoubleTextVM vm)
            => One(vm == null ? null : Join(vm.LeftLine, vm.RightLine));
    }

    public sealed class TripleTextBrickRenderer : TooltipBrickRenderer<TooltipBrickTripleTextVM>
    {
        // Flat path: all three columns on one line. (Left/Right come from the DoubleTextVM base.)
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickTripleTextVM vm)
            => One(vm == null ? null : Join(vm.LeftLine, vm.MiddleLine, vm.RightLine));

        // Tree: one node per column, left → middle → right (e.g. per-level saves: Fortitude, Reflex,
        // Will as three vertical nodes).
        public override IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm)
        {
            var t = vm as TooltipBrickTripleTextVM;
            if (t == null) yield break;
            if (!string.IsNullOrWhiteSpace(t.LeftLine)) yield return TooltipNode.Leaf(t.LeftLine.Trim());
            if (!string.IsNullOrWhiteSpace(t.MiddleLine)) yield return TooltipNode.Leaf(t.MiddleLine.Trim());
            if (!string.IsNullOrWhiteSpace(t.RightLine)) yield return TooltipNode.Leaf(t.RightLine.Trim());
        }
    }

    public sealed class IconAndNameBrickRenderer : TooltipBrickRenderer<TooltipBrickIconAndNameVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickIconAndNameVM vm)
            => One(vm?.Line, vm?.Tooltip); // icon may carry a nested tooltip → drill-in
    }

    public sealed class PortraitAndNameBrickRenderer : TooltipBrickRenderer<TooltipBrickPortraitAndNameVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickPortraitAndNameVM vm) => One(vm?.Line);
    }

    public sealed class IconNameDescBrickRenderer : TooltipBrickRenderer<TooltipBrickIconNameDescVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickIconNameDescVM vm)
            => One(vm == null ? null : Join(vm.Name, vm.Desc));
    }

    public sealed class FeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickFeatureVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickFeatureVM vm)
            => One(vm?.Name, vm?.Tooltip); // full feature/ability writeup on drill-in
    }

    public sealed class MultipleFeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleFeatureVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickMultipleFeatureVM vm)
        {
            if (vm?.TooltipBrickFeatures == null) yield break;
            foreach (var f in vm.TooltipBrickFeatures)
                if (f != null) yield return new TextElement(f.Name, null, f.Tooltip);
        }

        public override IEnumerable<UIElement> GetFlatElements(TooltipBrickMultipleFeatureVM vm)
            => One(vm?.TooltipBrickFeatures == null ? null
                : Join(vm.TooltipBrickFeatures.Where(f => f != null).Select(f => f.Name).ToArray()));
    }

    // A per-level feature in the archetype/first-level progression: like a feature (name + drill-in
    // write-up) plus the archetype's Added/Removed marker. It subclasses TooltipBrickFeatureVM, but
    // the registry keys on exact type, so it needs its own renderer (it won't reuse FeatureBrick's).
    public sealed class ArchetypeFeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickArchetypeFeatureVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickArchetypeFeatureVM vm) => One(vm?.Name, vm?.Tooltip);

        public override IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm)
        {
            var f = vm as TooltipBrickArchetypeFeatureVM;
            if (f == null || string.IsNullOrWhiteSpace(f.Name)) yield break;
            string anno = f.DifType == ClassArchetypeDifType.Added ? "added"
                        : f.DifType == ClassArchetypeDifType.Removed ? "removed" : null;
            yield return TooltipNode.Leaf(f.Name, annotation: anno, drillIn: f.Tooltip);
        }
    }

    public sealed class FeatureShortDescriptionBrickRenderer : TooltipBrickRenderer<TooltipBrickFeatureShortDescriptionVM>
    {
        // Name + short desc, with the feature's full write-up as a drill-in (e.g. signature features).
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickFeatureShortDescriptionVM vm)
            => One(vm == null ? null : Join(vm.Name, vm.Description), vm?.Tooltip);
    }

    public sealed class EntityHeaderBrickRenderer : TooltipBrickRenderer<TooltipBrickEntityHeaderVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickEntityHeaderVM vm)
            => One(vm == null ? null : Join(vm.MainTitle, vm.Title, vm.LeftLabel, vm.RightLabel));
    }

    public sealed class IconValueStatBrickRenderer : TooltipBrickRenderer<TooltipBrickIconValueStatVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickIconValueStatVM vm)
            => One(vm == null ? null : Stat(vm.Name, vm.Value, vm.Icon), vm?.Tooltip);
    }

    public sealed class MultipleIconValueStatBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleIconValueStatVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickMultipleIconValueStatVM vm)
        {
            if (vm?.TooltipBrickIconValueStats == null) yield break;
            foreach (var s in vm.TooltipBrickIconValueStats)
                if (s != null) yield return new TextElement(Stat(s.Name, s.Value, s.Icon), null, s.Tooltip);
        }

        public override IEnumerable<UIElement> GetFlatElements(TooltipBrickMultipleIconValueStatVM vm)
            => One(vm?.TooltipBrickIconValueStats == null ? null
                : Join(vm.TooltipBrickIconValueStats.Where(s => s != null).Select(s => Stat(s.Name, s.Value, s.Icon)).ToArray()));
    }

    public sealed class ValueStatFormulaBrickRenderer : TooltipBrickRenderer<TooltipBrickValueStatFormulaVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickValueStatFormulaVM vm)
            => One(vm == null ? null : Stat(vm.Name, vm.Value, null));
    }

    public sealed class TwoColumnsStatBrickRenderer : TooltipBrickRenderer<TooltipBrickTwoColumnsStatVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickTwoColumnsStatVM vm)
            => One(vm == null ? null : Join(Stat(vm.NameLeft, vm.ValueLeft, vm.IconLeft), Stat(vm.NameRight, vm.ValueRight, vm.IconRight)));

        // Tree: one node per column so each column's own drill-in tooltip is reachable.
        public override IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm)
        {
            var v = vm as TooltipBrickTwoColumnsStatVM;
            if (v == null) yield break;
            var l = Stat(v.NameLeft, v.ValueLeft, v.IconLeft);
            if (!string.IsNullOrWhiteSpace(l)) yield return TooltipNode.Leaf(l, drillIn: v.TooltipLeft);
            var r = Stat(v.NameRight, v.ValueRight, v.IconRight);
            if (!string.IsNullOrWhiteSpace(r)) yield return TooltipNode.Leaf(r, drillIn: v.TooltipRight);
        }
    }

    public sealed class ThreeColumnsStatBrickRenderer : TooltipBrickRenderer<TooltipBrickThreeColumnsStatVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickThreeColumnsStatVM vm)
            => One(vm == null ? null : Join(Stat(vm.NameLeft, vm.ValueLeft, vm.IconLeft),
                Stat(vm.NameCenter, vm.ValueCenter, vm.IconCenter), Stat(vm.NameRight, vm.ValueRight, vm.IconRight)));

        // Tree: one node per column, each carrying its column's drill-in tooltip.
        public override IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm)
        {
            var v = vm as TooltipBrickThreeColumnsStatVM;
            if (v == null) yield break;
            var l = Stat(v.NameLeft, v.ValueLeft, v.IconLeft);
            if (!string.IsNullOrWhiteSpace(l)) yield return TooltipNode.Leaf(l, drillIn: v.TooltipLeft);
            var c = Stat(v.NameCenter, v.ValueCenter, v.IconCenter);
            if (!string.IsNullOrWhiteSpace(c)) yield return TooltipNode.Leaf(c, drillIn: v.TooltipCenter);
            var r = Stat(v.NameRight, v.ValueRight, v.IconRight);
            if (!string.IsNullOrWhiteSpace(r)) yield return TooltipNode.Leaf(r, drillIn: v.TooltipRight);
        }
    }

    public sealed class AbilityTargetBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityTargetVM>
    {
        // Label + text, with its drill-in tooltip (the GetNodes bridge attaches the One()'s tooltip).
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickAbilityTargetVM vm)
            => One(vm == null ? null : Join(vm.Label, vm.Text), vm?.Tooltip);
    }

    public sealed class PictureBrickRenderer : TooltipBrickRenderer<TooltipBrickPictureVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickPictureVM vm)
            => One(vm?.Picture != null ? "Image: " + vm.Picture.name : null);
    }

    // Composite class build: difficulty, build-balance axes, then the description.
    public sealed class ShortClassDescriptionBrickRenderer : TooltipBrickRenderer<TooltipBrickShortClassDescriptionVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickShortClassDescriptionVM vm)
        {
            if (vm == null) yield break;

            var rate = vm.DifficultyRateVM;
            if (rate != null && !string.IsNullOrEmpty(rate.RateName))
                yield return new TextElement(rate.RateName + ": " + rate.Rate + " of " + rate.MaxRate);

            var bal = vm.ClassBalanceVM != null ? vm.ClassBalanceVM.ClassBalanceVM : null;
            if (bal != null)
            {
                string title = !string.IsNullOrEmpty(vm.BuildTitle) ? vm.BuildTitle : "Build balance";
                yield return new TextElement(title + ": Melee " + bal.Melee.Value
                    + ", Ranged " + bal.Ranged.Value + ", Defense " + bal.Defense.Value
                    + ", Support " + bal.Support.Value + ", Control " + bal.Control.Value
                    + ", Magic " + bal.Magic.Value);
            }

            string desc = vm.ClassDescriptionVM != null ? vm.ClassDescriptionVM.Text : null;
            if (!string.IsNullOrWhiteSpace(desc)) yield return new TextElement(desc);
        }
    }

    public sealed class AbilityScoresBlockBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityScoresBlockVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickAbilityScoresBlockVM vm)
        {
            var stats = vm?.AbilityScoresBlock?.AbilityScores;
            if (stats == null) yield break;
            foreach (var s in stats) { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new TextElement(line); }
        }

        public override IEnumerable<UIElement> GetFlatElements(TooltipBrickAbilityScoresBlockVM vm)
            => One(vm?.AbilityScoresBlock?.AbilityScores == null ? null
                : Join(vm.AbilityScoresBlock.AbilityScores.Select(StatLine).ToArray()));
    }

    public sealed class SkillsBrickRenderer : TooltipBrickRenderer<TooltipBrickSkillsVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickSkillsVM vm)
        {
            var stats = vm?.Skills?.Skills;
            if (stats == null) yield break;
            foreach (var s in stats) { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new TextElement(line); }
        }

        public override IEnumerable<UIElement> GetFlatElements(TooltipBrickSkillsVM vm)
            => One(vm?.Skills?.Skills == null ? null : Join(vm.Skills.Skills.Select(StatLine).ToArray()));
    }

    // Pure layout — nothing to read.
    public sealed class SeparatorBrickRenderer : TooltipBrickRenderer<TooltipBrickSeparatorVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickSeparatorVM vm) => None;
    }

    public sealed class SpaceBrickRenderer : TooltipBrickRenderer<TooltipBrickSpaceVM>
    {
        public override IEnumerable<UIElement> GetExpandedElements(TooltipBrickSpaceVM vm) => None;
    }
}
