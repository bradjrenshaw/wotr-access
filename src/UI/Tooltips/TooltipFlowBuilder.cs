using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Renders a game <see cref="TooltipBaseTemplate"/> into a <see cref="FlowSheet"/> — the DOCUMENT
    /// model. A template is a linear brick stream (effectively an HTML page: headings, paragraphs,
    /// label/value rows, tables, inline links), so we lay it out flat in ONE region:
    ///  • a <see cref="TooltipBrickTitleVM"/> becomes an inline HEADING ELEMENT — a text row read as
    ///    "&lt;title&gt;, heading level N" (N from its H1–H6 <see cref="TooltipTitleType"/>). It does NOT
    ///    start a group: heading-driven sectioning grouped bricks illogically (e.g. a spell's description
    ///    landing inside its "Descriptors" section), so headings are now just markers in the flat stream;
    ///  • every other brick contributes its renderer's elements as rows;
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
            var current = sheet.List(null); // ONE flat region holds the whole document (headings inline)
            int rows = 0;
            foreach (var el in Rows(template, type)) { current.Item(el); rows++; }
            if (rows == 0 && includeEmptyNotice)
                sheet.List(null).Item(new TextElement(() => Loc.T("tooltip.empty")));
            sheet.Reflow();
            return sheet;
        }

        /// <summary>The GRAPH twin: emit the document's rows as text nodes under
        /// <paramref name="keyPrefix"/>-indexed keys — same layout as <see cref="Build"/>, with Space
        /// following each row's own drill-in (template and/or inline glossary links) via
        /// <see cref="WrathAccess.Screens.TooltipScreen.FollowElement"/>. Returns the row count.</summary>
        public static int Emit(WrathAccess.UI.Graph.GraphBuilder b, string keyPrefix,
            TooltipBaseTemplate template, TooltipTemplateType type = TooltipTemplateType.Info,
            bool includeEmptyNotice = true)
        {
            int i = 0;
            foreach (var el in Rows(template, type))
            {
                var e = el;
                b.AddItem(WrathAccess.UI.Graph.ControlId.Structural(keyPrefix + "r" + i), new WrathAccess.UI.Graph.NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new WrathAccess.UI.Graph.NodeAnnouncement(() => e.GetFocusMessage().Resolve()) },
                    SearchText = e.GetLabelText,
                    // Every row offers Space: the handler resolves the drill targets LIVE (template +
                    // inline links) and speaks "no tooltip" when there are none — the old reader's UX.
                    OnTooltip = () => WrathAccess.Screens.TooltipScreen.FollowElement(e),
                });
                i++;
            }
            if (i == 0 && includeEmptyNotice)
            {
                b.AddItem(WrathAccess.UI.Graph.ControlId.Structural(keyPrefix + "empty"),
                    GraphNodes.Text(() => Loc.T("tooltip.empty")));
                i++;
            }
            return i;
        }

        // The document's rows, flat: headings inline, each brick's renderer elements, plain multi-line
        // text split one row per line (each reads on its own); link/label rows keep their drill-ins.
        private static IEnumerable<UIElement> Rows(TooltipBaseTemplate template, TooltipTemplateType type)
        {
            if (template == null) yield break;
            Prepare(template, type);
            foreach (var brick in Bricks(template, type))
            {
                var vm = SafeGetVM(brick);
                if (vm == null) continue;

                if (vm is TooltipBrickTitleVM title)
                {
                    // Inline heading element (NOT a new section): "<title>, heading level N", N from H1–H6.
                    if (!string.IsNullOrWhiteSpace(title.Title))
                        yield return new TextElement(title.Title, "heading_level", null,
                            new { level = (int)title.Type + 1 });
                    continue;
                }

                foreach (var el in TooltipBrickRegistry.Elements(vm, expanded: true))
                {
                    if (el == null || !el.CanFocus) continue;
                    if (el is TextElement te && !te.HasTooltip)
                    {
                        var text = te.GetText();
                        if (!string.IsNullOrEmpty(text) && text.IndexOf('\n') >= 0)
                        {
                            foreach (var part in text.Split('\n'))
                            {
                                var line = part.Trim();
                                if (line.Length > 0) yield return new TextElement(line);
                            }
                            continue;
                        }
                    }
                    yield return el;
                }
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
