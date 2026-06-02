using System;
using System.Collections.Generic;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Presents the whole character sheet as ONE <see cref="FlowSheet"/> (a single Tab-stop): each stat
    /// group or list section becomes a vertically-stacked region. Arrows move cell-to-cell across the
    /// whole sheet, Ctrl+Up/Down jump between sections. Replaces the bundle-of-tabbed-listboxes shape with
    /// a flat grid — easier to scan and search, faithful to how the game draws the sheet as one page.
    /// </summary>
    public sealed class FlowSheetCharSheetSink : ICharSheetSink
    {
        private readonly FlowSheet _sheet = new FlowSheet();

        // A multi-column group → a table region; a single-value group → a list region of "name, value"
        // lines. Either way the stat's drill-in tooltip is the region's row tooltip (Space).
        public void StatGroup(StatGroup g)
        {
            if (g == null || g.Rows.Count == 0) return;
            if (g.Columns.Length >= 2)
            {
                var table = _sheet.Table(g.Label, g.Columns);
                foreach (var row in g.Rows)
                {
                    var r = row; // capture for the live closures
                    table.Row(new TextElement(r.Name),
                        Array.ConvertAll(r.Values, v => (UIElement)new TextElement(v)),
                        tooltip: r.Tooltip);
                }
            }
            else
            {
                var list = _sheet.List(g.Label);
                foreach (var row in g.Rows)
                {
                    var r = row; // capture
                    list.Item(new TextElement(() => Compose(r)), tooltip: r.Tooltip);
                }
            }
        }

        public void ListSection(string label, IEnumerable<UIElement> items)
        {
            if (items == null) return;
            ListRegion list = null;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (list == null) list = _sheet.List(label); // created lazily → empty sections add nothing
                list.Item(it);
            }
        }

        public UIElement Build()
        {
            _sheet.Reflow();
            return _sheet;
        }

        // "Name, value" — the single value column folded into the spoken line.
        private static string Compose(StatRow r)
        {
            var name = r.Name != null ? r.Name() : null;
            var value = (r.Values.Length > 0 && r.Values[0] != null) ? r.Values[0]() : null;
            if (string.IsNullOrEmpty(value)) return name;
            return string.IsNullOrEmpty(name) ? value : name + ", " + value;
        }
    }
}
