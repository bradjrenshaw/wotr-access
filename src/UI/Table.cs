using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// A 2-D data grid (Excel-with-a-screen-reader). Arrows move a cell cursor — Left/Right change
    /// column, Up/Down change row — and the navigator announces the header for whichever axis changed
    /// plus the cell. The whole table is one Tab-stop. Column and row headers are real, navigable
    /// cells: they live at row -1 (the header row) and column -1 (the header column), so you can Up
    /// into the column headers or Left into the row headers. Missing cells are filled with a focusable
    /// "blank" so every position can be landed on (you hear "blank"). Cells are ordinary
    /// <see cref="UIElement"/>s (e.g. <see cref="TextElement"/> with a tooltip → Space drills in).
    /// </summary>
    public sealed class Table : Container
    {
        private readonly List<UIElement> _columnHeaders = new List<UIElement>();
        private readonly List<UIElement> _rowHeaders = new List<UIElement>();
        private readonly List<List<UIElement>> _cells = new List<List<UIElement>>(); // [row][col]

        public Table(string label = null) : base(ContainerShape.Grid, label) { }

        public bool HasColumnHeaders => _columnHeaders.Count > 0;
        public bool HasRowHeaders => _rowHeaders.Count > 0;
        public int RowCount => _cells.Count;
        public int ColCount => _columnHeaders.Count;

        /// <summary>Set the column headers (defines the column count — call before adding rows).</summary>
        public void SetColumnHeaders(IEnumerable<UIElement> headers)
        {
            foreach (var h in headers) { var e = h ?? Blank(); _columnHeaders.Add(e); Add(e); }
        }

        /// <summary>Add a data row: a row-header cell (may be null) + its cells (padded with blanks to
        /// the column count; null cells become blanks so every position is landable).</summary>
        public void AddRow(UIElement rowHeader, IEnumerable<UIElement> cells)
        {
            var rh = rowHeader ?? Blank();
            _rowHeaders.Add(rh); Add(rh);
            var row = new List<UIElement>();
            if (cells != null)
                foreach (var c in cells) { var e = c ?? Blank(); row.Add(e); Add(e); }
            while (row.Count < ColCount) { var e = Blank(); row.Add(e); Add(e); }
            _cells.Add(row);
        }

        // Coordinate access: r == -1 is the header row, c == -1 the header column. Out of range → null.
        public UIElement CellAt(int r, int c)
        {
            int minR = HasColumnHeaders ? -1 : 0;
            int minC = HasRowHeaders ? -1 : 0;
            if (r < minR || r >= RowCount || c < minC || c >= ColCount) return null;
            if (r == -1) return c >= 0 && c < _columnHeaders.Count ? _columnHeaders[c] : null; // corner (c==-1) → null
            if (c == -1) return r < _rowHeaders.Count ? _rowHeaders[r] : null;
            var row = _cells[r];
            return c < row.Count ? row[c] : null;
        }

        /// <summary>The (row, col) of an element in the grid, with -1 for header row/column. False if absent.</summary>
        public bool TryCoords(UIElement e, out int row, out int col)
        {
            for (int c = 0; c < _columnHeaders.Count; c++)
                if (_columnHeaders[c] == e) { row = -1; col = c; return true; }
            for (int r = 0; r < _rowHeaders.Count; r++)
                if (_rowHeaders[r] == e) { row = r; col = -1; return true; }
            for (int r = 0; r < _cells.Count; r++)
                for (int c = 0; c < _cells[r].Count; c++)
                    if (_cells[r][c] == e) { row = r; col = c; return true; }
            row = 0; col = 0; return false;
        }

        public string ColumnHeaderText(int c) =>
            c >= 0 && c < _columnHeaders.Count ? _columnHeaders[c].GetLabelText() : null;

        public string RowHeaderText(int r) =>
            r >= 0 && r < _rowHeaders.Count ? _rowHeaders[r].GetLabelText() : null;

        // Tab lands on the first data cell (you arrow up/left into headers from there).
        public override UIElement FirstFocusable() => CellAt(0, 0) ?? base.FirstFocusable();

        // Cell position is 2-D (the grid nav announces it), so don't tack on a flat "n of m".
        public override bool AnnouncePosition => false;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            yield return new RoleAnnouncement("table, " + RowCount + " rows by " + ColCount + " columns");
        }

        private static UIElement Blank() => new BlankCell();

        /// <summary>A focusable empty cell — read as "blank" by the grid navigator.</summary>
        private sealed class BlankCell : UIElement
        {
            public override bool CanFocus => true;
        }
    }
}
