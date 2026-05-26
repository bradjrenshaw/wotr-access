using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Turns a game <see cref="TooltipBaseTemplate"/> into a tree of <see cref="TooltipNode"/>s — the
    /// structured replacement for the old flat <c>TooltipReader.Build/BuildSections</c>. Grouping is
    /// driven by title <b>rank</b>: a <see cref="TooltipBrickTitleVM"/> of rank R (H1=1 … H6=6) opens a
    /// group at R; lower ranks nest under it; an equal/higher-rank title closes back to that level. All
    /// other bricks contribute their renderer's <c>GetNodes</c> to the current group (or the root if no
    /// title is open). Separators/spaces are structural and dropped. Rendered with
    /// <see cref="TooltipTemplateType.Info"/> (the panel form) by default — the larger of the two
    /// template forms (e.g. a class's first-level template only emits per-level features at Info).
    /// </summary>
    public static class TooltipTreeBuilder
    {
        public static List<TooltipNode> Build(TooltipBaseTemplate template,
            TooltipTemplateType type = TooltipTemplateType.Info)
        {
            var roots = new List<TooltipNode>();
            if (template == null) return roots;
            Prepare(template, type);

            // Open groups as (rank, node); root = rank 0.
            var stack = new List<KeyValuePair<int, TooltipNode>>();

            void AddNode(TooltipNode n)
            {
                if (stack.Count > 0) stack[stack.Count - 1].Value.Add(n);
                else roots.Add(n);
            }

            foreach (var brick in Bricks(template, type))
            {
                var vm = SafeGetVM(brick);
                if (vm == null) continue;

                if (vm is TooltipBrickTitleVM title)
                {
                    int rank = (int)title.Type + 1; // H1 → 1 … H6 → 6
                    while (stack.Count > 0 && stack[stack.Count - 1].Key >= rank)
                        stack.RemoveAt(stack.Count - 1);
                    var group = TooltipNode.Branch(title.Title, "heading");
                    AddNode(group);
                    stack.Add(new KeyValuePair<int, TooltipNode>(rank, group));
                    continue;
                }

                foreach (var n in TooltipBrickRegistry.Nodes(vm))
                    if (n != null) AddNode(n);
            }

            return roots;
        }

        /// <summary>
        /// Expand the structural skeleton — groups whose children are already built (headings,
        /// sub-groups) — recursively, while leaving lazy drill-in nodes (feature write-ups, glossary
        /// links) collapsed. Use for surfaces that should read fully on open (a tooltip panel); other
        /// surfaces (settings) start collapsed so a group can be skipped with one step.
        /// </summary>
        public static void ExpandStructural(Container root)
        {
            if (root == null) return;
            foreach (var child in root.Children)
                if (child is Container c && c.Shape == ContainerShape.Tree && c.Children.Count > 0)
                {
                    c.Expand();              // eager children only — does not trigger a lazy factory
                    ExpandStructural(c);
                }
        }

        private static IEnumerable<ITooltipBrick> Bricks(TooltipBaseTemplate t, TooltipTemplateType type)
        {
            foreach (var b in Section(t, x => x.GetHeader(type))) yield return b;
            foreach (var b in Section(t, x => x.GetBody(type))) yield return b;
            foreach (var b in Section(t, x => x.GetFooter(type))) yield return b;
        }

        private static IEnumerable<ITooltipBrick> Section(TooltipBaseTemplate t,
            Func<TooltipBaseTemplate, IEnumerable<ITooltipBrick>> get)
        {
            try { return get(t) ?? Array.Empty<ITooltipBrick>(); }
            catch (Exception e) { Main.Log?.Error("TooltipTreeBuilder section: " + e.Message); return Array.Empty<ITooltipBrick>(); }
        }

        private static TooltipBaseBrickVM SafeGetVM(ITooltipBrick brick)
        {
            if (brick == null) return null;
            try { return brick.GetVM(); }
            catch (Exception e) { Main.Log?.Error("TooltipTreeBuilder GetVM: " + e.Message); return null; }
        }

        private static void Prepare(TooltipBaseTemplate template, TooltipTemplateType type)
        {
            try { template.Prepare(type); }
            catch (Exception e) { Main.Log?.Error("TooltipTreeBuilder.Prepare: " + e.Message); }
        }
    }
}
