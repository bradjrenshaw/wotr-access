using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>One titled group of tooltip elements — a section the player tabs to.</summary>
    public sealed class TooltipSection
    {
        public readonly string Title;
        public readonly List<UIElement> Elements = new List<UIElement>();
        public TooltipSection(string title) { Title = title; }
    }

    /// <summary>
    /// Assembles a game TooltipBaseTemplate into navigable elements via the per-brick renderers
    /// (<see cref="TooltipBrickRegistry"/>). Two layouts from the same renderers: <see cref="Build"/>
    /// returns a flat element list (the standalone tooltip screen); <see cref="BuildSections"/>
    /// splits the body at title bricks into sections (header + lead = the first section) for inline
    /// detail panels. <paramref name="expanded"/> chooses each brick's granular vs condensed form.
    /// </summary>
    public static class TooltipReader
    {
        public static List<UIElement> Build(TooltipBaseTemplate template, bool expanded = true)
        {
            var result = new List<UIElement>();
            if (template == null) return result;

            Prepare(template);
            AddFlat(Get(template, t => t.GetHeader(TooltipTemplateType.Tooltip)), result, expanded);
            AddFlat(Get(template, t => t.GetBody(TooltipTemplateType.Tooltip)), result, expanded);
            AddFlat(Get(template, t => t.GetFooter(TooltipTemplateType.Tooltip)), result, expanded);
            return result;
        }

        public static List<TooltipSection> BuildSections(TooltipBaseTemplate template, bool expanded = true)
        {
            var sections = new List<TooltipSection>();
            if (template == null) return sections;

            Prepare(template);
            var current = new TooltipSection(null);
            sections.Add(current);
            current = Collect(Get(template, t => t.GetHeader(TooltipTemplateType.Tooltip)), sections, current, expanded, allowBreaks: false);
            current = Collect(Get(template, t => t.GetBody(TooltipTemplateType.Tooltip)), sections, current, expanded, allowBreaks: true);
            current = Collect(Get(template, t => t.GetFooter(TooltipTemplateType.Tooltip)), sections, current, expanded, allowBreaks: true);
            return sections.Where(s => s.Elements.Count > 0).ToList();
        }

        private static void Prepare(TooltipBaseTemplate template)
        {
            try { template.Prepare(TooltipTemplateType.Tooltip); }
            catch (Exception e) { Main.Log?.Error("TooltipReader.Prepare: " + e.Message); }
        }

        private static IEnumerable<ITooltipBrick> Get(TooltipBaseTemplate t, Func<TooltipBaseTemplate, IEnumerable<ITooltipBrick>> get)
        {
            try { return get(t); }
            catch (Exception e) { Main.Log?.Error("TooltipReader section: " + e.Message); return null; }
        }

        private static void AddFlat(IEnumerable<ITooltipBrick> bricks, List<UIElement> result, bool expanded)
        {
            if (bricks == null) return;
            foreach (var vm in Vms(bricks))
                foreach (var el in TooltipBrickRegistry.Elements(vm, expanded))
                    if (el != null && el.CanFocus) result.Add(el);
        }

        private static TooltipSection Collect(IEnumerable<ITooltipBrick> bricks, List<TooltipSection> sections,
            TooltipSection current, bool expanded, bool allowBreaks)
        {
            if (bricks == null) return current;
            foreach (var vm in Vms(bricks))
            {
                if (allowBreaks && vm is TooltipBrickTitleVM title)
                {
                    current = new TooltipSection(title.Title);
                    sections.Add(current);
                    continue;
                }
                foreach (var el in TooltipBrickRegistry.Elements(vm, expanded))
                    if (el != null && el.CanFocus) current.Elements.Add(el);
            }
            return current;
        }

        private static IEnumerable<TooltipBaseBrickVM> Vms(IEnumerable<ITooltipBrick> bricks)
        {
            foreach (var brick in bricks)
            {
                if (brick == null) continue;
                TooltipBaseBrickVM vm;
                try { vm = brick.GetVM(); }
                catch (Exception e) { Main.Log?.Error("TooltipReader GetVM: " + e.Message); continue; }
                if (vm != null) yield return vm;
            }
        }
    }
}
