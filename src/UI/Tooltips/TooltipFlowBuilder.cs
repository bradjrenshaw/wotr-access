using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Renders a game <see cref="TooltipBaseTemplate"/> into a <see cref="FlowSheet"/> — the DOCUMENT
    /// model. A template is a linear brick stream (effectively an HTML page: headings, paragraphs,
    /// label/value rows, tables, inline links), so we lay it out flat instead of forcing a tree:
    ///  • a <see cref="TooltipBrickTitleVM"/> starts a new titled section — a <see cref="ListRegion"/>
    ///    whose label is the heading (announced on entry, jumpable with Ctrl+Up/Down). A heading no
    ///    longer "contains" the bricks after it; it just ends where the next heading begins, matching
    ///    the visual layout;
    ///  • every other brick contributes its renderer's elements as rows in the current section;
    ///  • plain (link-free) multi-line text — a class's skill list packed into one brick — splits into
    ///    one row per line so each reads on its own.
    /// Drill-in rides each row element's own <see cref="UIElement.GetTooltipTemplate"/>: Space FOLLOWS
    /// it to a new page (the <see cref="WrathAccess.Screens.TooltipScreen"/> stack), nothing expands
    /// inline — which is why the old tree's repeating-headers / cycle guards are gone. Read at
    /// <see cref="TooltipTemplateType.Info"/> (the larger panel form).
    /// </summary>
    public static class TooltipFlowBuilder
    {
        /// <param name="includeEmptyNotice">When the template yields no rows, add a single
        /// "No tooltip information" row (true, for the standalone reader where Space must show
        /// something) or leave the sheet empty (false, for embedded detail panels that just want
        /// nothing — the caller checks <see cref="FlowSheet.RowCount"/> and skips adding it).</param>
        public static FlowSheet Build(TooltipBaseTemplate template,
            TooltipTemplateType type = TooltipTemplateType.Info, bool includeEmptyNotice = true)
        {
            var sheet = new FlowSheet();
            if (template == null) return sheet;
            Prepare(template, type);

            var current = sheet.List(null); // lead section: header bricks before the first title
            int rows = 0;

            foreach (var brick in Bricks(template, type))
            {
                var vm = SafeGetVM(brick);
                if (vm == null) continue;

                if (vm is TooltipBrickTitleVM title)
                {
                    if (!string.IsNullOrWhiteSpace(title.Title)) current = sheet.List(title.Title);
                    continue;
                }

                foreach (var el in TooltipBrickRegistry.Elements(vm, expanded: true))
                    rows += AddRows(current, el);
            }

            if (rows == 0 && includeEmptyNotice)
                sheet.List(null).Item(new TextElement(() => Loc.T("tooltip.empty")));
            sheet.Reflow();
            return sheet;
        }

        // One element → one or more rows. A plain (drill-in-free) multi-line text brick splits into a
        // row per line; everything else (link/name rows, label/value rows) stays one row, keeping its
        // own drill-in tooltip for Space-to-follow.
        private static int AddRows(ListRegion region, UIElement el)
        {
            if (el == null || !el.CanFocus) return 0;
            if (el is TextElement te && !te.HasTooltip)
            {
                var text = te.GetText();
                if (!string.IsNullOrEmpty(text) && text.IndexOf('\n') >= 0)
                {
                    int n = 0;
                    foreach (var part in text.Split('\n'))
                    {
                        var line = part.Trim();
                        if (line.Length == 0) continue;
                        region.Item(new TextElement(line));
                        n++;
                    }
                    return n;
                }
            }
            region.Item(el);
            return 1;
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
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder section: " + e.Message); return Array.Empty<ITooltipBrick>(); }
        }

        private static TooltipBaseBrickVM SafeGetVM(ITooltipBrick brick)
        {
            if (brick == null) return null;
            try { return brick.GetVM(); }
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder GetVM: " + e.Message); return null; }
        }

        private static void Prepare(TooltipBaseTemplate template, TooltipTemplateType type)
        {
            try { template.Prepare(type); }
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder.Prepare: " + e.Message); }
        }
    }
}
