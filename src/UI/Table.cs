using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>What a grid cell acts as for nearest-header lookup (a cell may also be plain data).</summary>
    public enum CellRole { None, Column, Row, Group }

    /// <summary>
    /// A 2-D data grid (Excel-with-a-screen-reader). Arrows move a cell cursor — Left/Right change
    /// column, Up/Down change row — and the navigator announces the header for whichever axis changed
    /// plus the cell. The whole table is one Tab-stop.
    ///
    /// Headers are NOT confined to the edges: any cell can be flagged a <see cref="CellRole.Column"/>,
    /// <see cref="CellRole.Row"/>, or <see cref="CellRole.Group"/> header, and a data cell resolves its
    /// headers by proximity — column header = nearest Column cell scanning UP its column; row header =
    /// nearest Row cell scanning LEFT along its row; group = nearest Group cell scanning UP column 0.
    /// That lets many blocks live in one grid: each block restates its own "Level 1…N" header row (with
    /// the group name in the leftmost cell) followed by its data rows, and every cell binds to whichever
    /// headers are nearest. Missing data cells are filled with a focusable "blank". Cells are ordinary
    /// <see cref="UIElement"/>s (e.g. <see cref="TextElement"/> with a tooltip → Space drills in).
    /// </summary>
    public sealed class Table : Container
    {
        private struct Cell { public UIElement Element; public CellRole Role; }

        private readonly List<List<Cell>> _rows = new List<List<Cell>>(); // [r][c]
        private int _cols;

        public Table(string label = null) : base(ContainerShape.Grid, label) { }

        public int RowCount => _rows.Count;
        public int ColCount => _cols;

        /// <summary>A block header row: the group name in the leftmost cell, then the column headers
        /// (e.g. the level ruler). Restate this at the top of every block.</summary>
        public void AddHeaderRow(UIElement group, IEnumerable<UIElement> columnHeaders)
        {
            var row = new List<Cell> { Make(group, CellRole.Group) };
            if (columnHeaders != null)
                foreach (var h in columnHeaders) row.Add(Make(h, CellRole.Column));
            Append(row);
        }

        /// <summary>A data row: the row label in the leftmost cell, then its cells (null → blank).</summary>
        public void AddDataRow(UIElement rowLabel, IEnumerable<UIElement> cells)
        {
            var row = new List<Cell> { Make(rowLabel, CellRole.Row) };
            if (cells != null)
                foreach (var c in cells) row.Add(Make(c, CellRole.None));
            Append(row);
        }

        private Cell Make(UIElement e, CellRole role)
        {
            var el = e ?? new BlankCell();
            Add(el); // register as a focusable child so the navigator can path to it
            return new Cell { Element = el, Role = role };
        }

        // Grow to the widest row, back-padding shorter rows with blanks so every position is landable.
        private void Append(List<Cell> row)
        {
            _rows.Add(row);
            if (row.Count > _cols) { _cols = row.Count; foreach (var r in _rows) Pad(r); }
            else Pad(row);
        }

        private void Pad(List<Cell> row)
        {
            while (row.Count < _cols) row.Add(Make(null, CellRole.None));
        }

        public UIElement CellAt(int r, int c)
        {
            if (r < 0 || r >= _rows.Count || c < 0 || c >= _rows[r].Count) return null;
            return _rows[r][c].Element;
        }

        public CellRole RoleAt(int r, int c)
        {
            if (r < 0 || r >= _rows.Count || c < 0 || c >= _rows[r].Count) return CellRole.None;
            return _rows[r][c].Role;
        }

        /// <summary>The (row, col) of an element, or false if absent.</summary>
        public bool TryCoords(UIElement e, out int row, out int col)
        {
            for (int r = 0; r < _rows.Count; r++)
                for (int c = 0; c < _rows[r].Count; c++)
                    if (_rows[r][c].Element == e) { row = r; col = c; return true; }
            row = 0; col = 0; return false;
        }

        // Nearest Column header scanning UP column c (excludes the cell itself).
        public string ColumnHeaderText(int r, int c)
        {
            for (int rr = r - 1; rr >= 0; rr--)
                if (RoleAt(rr, c) == CellRole.Column) return CellAt(rr, c)?.GetLabelText();
            return null;
        }

        // Nearest Row header scanning LEFT along row r (excludes the cell itself).
        public string RowHeaderText(int r, int c)
        {
            for (int cc = c - 1; cc >= 0; cc--)
                if (RoleAt(r, cc) == CellRole.Row) return CellAt(r, cc)?.GetLabelText();
            return null;
        }

        // Nearest Group header scanning UP column 0 (includes the cell's own row, so a group-header
        // cell reports itself and a data cell finds its block's header above).
        public string GroupText(int r, int c)
        {
            for (int rr = r; rr >= 0; rr--)
                if (RoleAt(rr, 0) == CellRole.Group) return CellAt(rr, 0)?.GetLabelText();
            return null;
        }

        public override UIElement FirstFocusable() => CellAt(0, 0) ?? base.FirstFocusable();

        // Cell position is 2-D (the grid nav announces it), so don't tack on a flat "n of m".
        public override bool AnnouncePosition => false;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            yield return new RoleAnnouncement("table, " + RowCount + " rows by " + ColCount + " columns");
        }

        /// <summary>A focusable empty cell — read as "blank" by the grid navigator.</summary>
        private sealed class BlankCell : UIElement
        {
            public override bool CanFocus => true;
        }
    }
}
