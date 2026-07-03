using System;
using System.Collections.Generic;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// The graph-native table/document emitter — the FlowSheet idiom rebuilt on graph primitives, with
    /// no special composer: one Tab-stop of vertically-stacked REGIONS (each a Ctrl+arrow jump target
    /// and a context level, so entering one announces its title once via the path diff), rows navigated
    /// Up/Down with the column preserved, cells Left/Right. The old framing rules ride the graph's own
    /// mechanisms:
    ///  - column header on column change  = left/right EDGE LABELS (the destination column's header);
    ///  - row name when off in a metadata column = vertical edge labels into non-primary cells;
    ///  - the associated-element readout = the PRIMARY (column 0) cell's announcement list carrying the
    ///    row's metadata as extra parts — vertical navigation rides column 0, so moving down the table
    ///    reads the whole row, per-part filterable like any control;
    ///  - empty cells read the localized "blank".
    /// Emit rows in one region, then start the next; Finish() closes the last region. Raw mode
    /// underneath (explicit edges), so no auto positions — matching the old sheets.
    /// </summary>
    public sealed class GraphSheet
    {
        private readonly GraphBuilder _b;
        private readonly string _key;
        private int _regionIndex = -1;
        private bool _contextOpen;

        // Current region state.
        private string[] _columns; // headers for cells 1..N (null = a plain list region)
        private int _row = -1;
        private List<ControlId> _prevRowIds;
        private List<ControlId> _rowIds;
        private Func<string> _rowName; // the current row's primary label (for vertical edge labels)
        private Func<string> _prevRowName;

        public GraphSheet(GraphBuilder b, string keyPrefix)
        {
            _b = b;
            _key = keyPrefix;
        }

        /// <summary>Start a region: a Ctrl+arrow jump target and a context level ("Buy cart, table").
        /// <paramref name="columns"/> are the headers for the metadata cells (column 0 — the primary —
        /// has none); null/empty = a plain one-column list region.</summary>
        public GraphSheet Region(string label, string[] columns = null, string role = null)
        {
            CloseRegion();
            _regionIndex++;
            _b.SetRegion(_key + "reg:" + _regionIndex);
            if (!string.IsNullOrEmpty(label))
            {
                _b.PushContext(label, role ?? (columns != null && columns.Length > 0 ? Loc.T("role.table") : null),
                    positions: false);
                _contextOpen = true;
            }
            _columns = columns;
            return this;
        }

        /// <summary>One row: the interactive/primary cell's vtable plus the metadata cell values (their
        /// count should match the region's columns). Metadata cells are read-only text.</summary>
        public GraphSheet Row(NodeVtable primary, params Func<string>[] cells)
        {
            _row++;
            _prevRowIds = _rowIds;
            _prevRowName = _rowName;
            _rowIds = new List<ControlId>();

            // The row's name for vertical edge labels = the primary's label (first announcement part).
            var primaryLabel = primary.Announcements != null && primary.Announcements.Count > 0
                ? primary.Announcements[0].Text : null;
            _rowName = primaryLabel;

            EmitCell(primary, 0);
            if (cells != null)
                for (int i = 0; i < cells.Length; i++)
                {
                    var v = cells[i];
                    EmitCell(new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => Blank(v?.Invoke())) },
                        SearchText = _rowName, // type-ahead matches the row's name from any cell
                    }, i + 1);
                }
            WireVertical();
            return this;
        }

        /// <summary>A single full-width line (a lead row like "Your gold", a section note).</summary>
        public GraphSheet Line(NodeVtable vt)
        {
            _row++;
            _prevRowIds = _rowIds;
            _prevRowName = _rowName;
            _rowIds = new List<ControlId>();
            _rowName = vt.Announcements != null && vt.Announcements.Count > 0 ? vt.Announcements[0].Text : null;
            EmitCell(vt, 0);
            WireVertical();
            return this;
        }

        /// <summary>Close the final region. Call once after the last row.</summary>
        public void Finish() => CloseRegion();

        private void CloseRegion()
        {
            if (_contextOpen) { _b.PopContext(); _contextOpen = false; }
            // Rows don't chain across region boundaries here — they do: the last row of a region wires
            // to the first of the next as rows are emitted (prev-row linkage carries across Region()).
            _columns = null;
        }

        private void EmitCell(NodeVtable vt, int col)
        {
            var id = ControlId.Structural(_key + "r" + _row + "c" + col);
            _b.AddNode(id, vt);
            _rowIds.Add(id);

            // Left/right within the row, labeled with the DESTINATION column's header (none onto the
            // primary — its own full readout identifies it).
            if (col > 0)
            {
                var leftId = _rowIds[col - 1];
                _b.Connect(id, GraphDir.Left, leftId, col - 1 == 0 ? null : Header(col - 1));
                _b.Connect(leftId, GraphDir.Right, id, Header(col));
            }
        }

        // Vertical edges between the completed row and the previous one: same column where both rows
        // have it, else the other row's primary (ragged rows never dead-end). Labels name the
        // destination ROW when landing off-primary (so you know which row you're in without the full
        // readout); landings on column 0 stay unlabeled — the primary's parts read the whole row.
        private void WireVertical()
        {
            if (_prevRowIds == null || _prevRowIds.Count == 0) return;
            int cols = Math.Max(_rowIds.Count, _prevRowIds.Count);
            for (int col = 0; col < cols; col++)
            {
                var down = col < _rowIds.Count ? _rowIds[col] : _rowIds[0];
                var up = col < _prevRowIds.Count ? _prevRowIds[col] : _prevRowIds[0];
                if (col < _prevRowIds.Count)
                    _b.Connect(up, GraphDir.Down, down, col < _rowIds.Count && col > 0 ? Text(_rowName) : null);
                if (col < _rowIds.Count)
                    _b.Connect(down, GraphDir.Up, up, col < _prevRowIds.Count && col > 0 ? Text(_prevRowName) : null);
            }
        }

        private string Header(int col)
            => _columns != null && col - 1 >= 0 && col - 1 < _columns.Length ? _columns[col - 1] : null;

        private static string Text(Func<string> f) => f?.Invoke();

        private static string Blank(string v) => string.IsNullOrWhiteSpace(v) ? Loc.T("value.blank") : v;
    }
}
