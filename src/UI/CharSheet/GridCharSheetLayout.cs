using System.Collections.Generic;
using System.Linq;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// RETAINED ALTERNATE PRESENTATION — only reachable via <see cref="PanelCharSheetSink"/>
    /// (not instantiated anywhere today; FlowSheetCharSheetSink is the live default). A group with two or more value columns (ability scores,
    /// skills) becomes a <see cref="Table"/> grid — arrow across columns / down rows, Space on a row
    /// drills into that stat's modifier breakdown. A single-value group (saving throws, defenses)
    /// becomes a flat <see cref="ListContainer"/> of "name, value" lines, each carrying the same
    /// drill-in tooltip. Either way the group is one Tab-stop, so you Tab between groups and arrow
    /// within.
    /// </summary>
    public sealed class GridCharSheetLayout : ICharSheetLayout
    {
        public UIElement Build(StatGroup group)
            => group.Columns.Length >= 2 ? (UIElement)BuildGrid(group) : BuildList(group);

        private static Table BuildGrid(StatGroup g)
        {
            var table = new Table(g.Label);
            table.AddHeaderRow(new TextElement(g.Label, "heading"),
                g.Columns.Select(c => (UIElement)new TextElement(c)));
            foreach (var row in g.Rows)
            {
                var r = row; // capture for the live closures
                table.AddDataRow(new TextElement(r.Name),
                    r.Values.Select(v => (UIElement)new TextElement(v)),
                    rowTooltip: () => r.Tooltip != null ? r.Tooltip() : null);
            }
            return table;
        }

        private static ListContainer BuildList(StatGroup g)
        {
            var list = new ListContainer(g.Label);
            foreach (var row in g.Rows)
            {
                var r = row; // capture
                list.Add(new TextElement(() => Compose(r), tooltip: () => r.Tooltip != null ? r.Tooltip() : null));
            }
            return list;
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
