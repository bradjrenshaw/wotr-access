using System;
using System.Collections.Generic;

namespace WrathAccess.UI.Graph
{
    /// <summary>
    /// Builds a <see cref="GraphRender"/>. Two construction styles (mixing them is an error):
    ///
    /// <para><b>Menu mode</b> — rows of controls, wired automatically: left/right within a row, up/down
    /// between consecutive rows (two rows sharing a non-null row key get column navigation — up/down
    /// preserves the position instead of snapping to the first item; ported from Tanglebeep's
    /// MenuBuilder, itself from Factorio Access's menu.lua). Items added outside an explicit row become
    /// single-item rows (a plain vertical menu).</para>
    ///
    /// <para><b>Raw mode</b> — <see cref="AddNode"/> + <see cref="Connect"/> for arbitrary topologies.</para>
    ///
    /// Orthogonal to both: <see cref="BeginStop"/> groups nodes into Tab-stops (arrows never cross a stop;
    /// Tab cycles them), <see cref="SetRegion"/> tags nodes with a region for Ctrl+arrow jumps, and
    /// <see cref="PushContext"/> stacks the presentation hierarchy spoken when focus enters from outside
    /// ("Difficulty settings, list, …").
    /// </summary>
    public sealed class GraphBuilder
    {
        private sealed class Entry
        {
            public ControlId Id;
            public NodeVtable Vtable;
            public ContextEntry[] Context;
            public object StopKey;
            public object RegionKey;
        }

        private sealed class Row
        {
            public readonly List<Entry> Items = new List<Entry>();
            public object Key;
            public object StopKey;
        }

        private sealed class RawEdge
        {
            public ControlId From;
            public GraphDir Dir;
            public ControlId To;
            public string Label;
        }

        // Menu mode.
        private readonly List<Row> _rows = new List<Row>();
        private Row _currentRow;

        // Raw mode (mutually exclusive with menu mode).
        private readonly List<Entry> _rawNodes = new List<Entry>();
        private readonly List<RawEdge> _rawEdges = new List<RawEdge>();

        // Shared.
        private readonly HashSet<ControlId> _ids = new HashSet<ControlId>();
        private ControlId _start;

        // Stop / region / context state applied to nodes as they are added.
        private object _stopKey = AutoStopKey(0);
        private int _stopAuto = 1;
        private object _regionKey;
        private readonly List<ContextEntry> _contextStack = new List<ContextEntry>();
        private ContextEntry[] _contextSnapshot = GraphNode.EmptyContext;

        private static object AutoStopKey(int index) => "stop#" + index;

        // ---- stops / regions / contexts ----

        /// <summary>Start a new Tab-stop; nodes added from here belong to it. <paramref name="key"/> must be
        /// stable across rebuilds (it keys the stop's remembered position); null auto-assigns by index,
        /// which is stable when the screen builds its stops in a fixed order.</summary>
        public GraphBuilder BeginStop(object key = null)
        {
            if (_currentRow != null) throw new InvalidOperationException("Cannot begin a stop inside an open row");
            _stopKey = key ?? AutoStopKey(_stopAuto);
            _stopAuto++;
            _regionKey = null; // regions are per-stop
            return this;
        }

        /// <summary>Tag nodes added from here with a region (Ctrl+arrow jump target) within the current
        /// stop; null clears. Region keys must be stable across rebuilds.</summary>
        public GraphBuilder SetRegion(object key)
        {
            _regionKey = key;
            return this;
        }

        /// <summary>Push one level of presentation hierarchy ("Difficulty settings", "list") onto nodes
        /// added from here. The announcer reads newly-entered levels when focus comes in from outside.</summary>
        public GraphBuilder PushContext(string label, string role = null)
        {
            _contextStack.Add(new ContextEntry(label, role));
            _contextSnapshot = null;
            return this;
        }

        public GraphBuilder PopContext()
        {
            if (_contextStack.Count == 0) throw new InvalidOperationException("No context to pop");
            _contextStack.RemoveAt(_contextStack.Count - 1);
            _contextSnapshot = null;
            return this;
        }

        private ContextEntry[] ContextSnapshot()
        {
            if (_contextSnapshot == null)
                _contextSnapshot = _contextStack.Count == 0 ? GraphNode.EmptyContext : _contextStack.ToArray();
            return _contextSnapshot;
        }

        /// <summary>Focus starts here when the graph has no prior position (defaults to the first node).</summary>
        public GraphBuilder SetStart(ControlId id)
        {
            _start = id;
            return this;
        }

        // ---- menu mode ----

        /// <summary>Open a horizontal row. Rows sharing a non-null <paramref name="rowKey"/> with the row
        /// above/below get column-preserving vertical navigation.</summary>
        public GraphBuilder StartRow(object rowKey = null)
        {
            if (_currentRow != null) throw new InvalidOperationException("Cannot start a row while another is open");
            _currentRow = new Row { Key = rowKey, StopKey = _stopKey };
            return this;
        }

        public GraphBuilder EndRow()
        {
            if (_currentRow == null) throw new InvalidOperationException("No row to end");
            if (_currentRow.Items.Count == 0) throw new InvalidOperationException("Row cannot be empty");
            _rows.Add(_currentRow);
            _currentRow = null;
            return this;
        }

        /// <summary>Add a control — into the open row, or as its own single-item row.</summary>
        public GraphBuilder AddItem(ControlId id, NodeVtable vtable)
        {
            var entry = MakeEntry(id, vtable);
            if (_currentRow != null)
            {
                _currentRow.Items.Add(entry);
            }
            else
            {
                var row = new Row { StopKey = _stopKey };
                row.Items.Add(entry);
                _rows.Add(row);
            }
            return this;
        }

        /// <summary>Add a read-only line (label only; no actions).</summary>
        public GraphBuilder AddLabel(ControlId id, Func<string> label)
            => AddItem(id, new NodeVtable { Label = label });

        // ---- raw mode ----

        /// <summary>Add a node with no automatic wiring (raw mode; wire with <see cref="Connect"/>).</summary>
        public GraphBuilder AddNode(ControlId id, NodeVtable vtable)
        {
            _rawNodes.Add(MakeEntry(id, vtable));
            return this;
        }

        /// <summary>Directed edge <paramref name="from"/> → <paramref name="to"/>, with an optional spoken
        /// transition line ("lane change"). Edges to/from undeclared nodes are dropped at build.</summary>
        public GraphBuilder Connect(ControlId from, GraphDir dir, ControlId to, string label = null)
        {
            if (from == null || to == null)
                throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
            _rawEdges.Add(new RawEdge { From = from, Dir = dir, To = to, Label = label });
            return this;
        }

        private Entry MakeEntry(ControlId id, NodeVtable vtable)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (vtable == null || vtable.Label == null)
                throw new ArgumentException("A control must have a Label", nameof(vtable));
            if (!_ids.Add(id)) throw new InvalidOperationException("Duplicate control id: " + id);
            return new Entry
            {
                Id = id,
                Vtable = vtable,
                Context = ContextSnapshot(),
                StopKey = _stopKey,
                RegionKey = _regionKey,
            };
        }

        // ---- build ----

        /// <summary>Finalize into a render, or null when nothing was declared (treat as "closed").</summary>
        public GraphRender Build()
        {
            if (_currentRow != null) throw new InvalidOperationException("Unclosed row - call EndRow()");
            bool hasRaw = _rawNodes.Count > 0;
            bool hasMenu = _rows.Count > 0;
            if (hasRaw && hasMenu)
                throw new InvalidOperationException("Cannot mix raw graph (AddNode/Connect) with menu rows in one build");
            if (!hasRaw && !hasMenu) return null;

            var render = new GraphRender();
            if (hasRaw)
            {
                foreach (var e in _rawNodes) AddNodeTo(render, e);
                foreach (var e in _rawEdges)
                    if (render.Nodes.ContainsKey(e.From) && render.Nodes.ContainsKey(e.To))
                        render.Nodes[e.From].Transitions[e.Dir] = new Transition(e.To, e.Label);
            }
            else
            {
                foreach (var row in _rows)
                    foreach (var e in row.Items)
                        AddNodeTo(render, e);
                WireMenuEdges(render);
            }

            render.StartKey = _start != null && render.Nodes.ContainsKey(_start)
                ? _start
                : render.Order[0].Id;
            return render;
        }

        private static void AddNodeTo(GraphRender render, Entry e)
        {
            var node = new GraphNode
            {
                Id = e.Id,
                Vtable = e.Vtable,
                Context = e.Context,
                StopKey = e.StopKey,
                RegionKey = e.RegionKey,
            };
            render.Nodes.Add(e.Id, node);
            render.Order.Add(node);
        }

        // Left/right within a row; up/down between consecutive rows OF THE SAME STOP (arrows never cross a
        // Tab-stop). Shared non-null row keys preserve the column; otherwise vertical lands on first item.
        private void WireMenuEdges(GraphRender render)
        {
            var byStop = new List<List<Row>>();
            var stopIndex = new Dictionary<object, int>();
            foreach (var row in _rows)
            {
                int idx;
                if (!stopIndex.TryGetValue(row.StopKey, out idx))
                {
                    idx = byStop.Count;
                    stopIndex.Add(row.StopKey, idx);
                    byStop.Add(new List<Row>());
                }
                byStop[idx].Add(row);
            }

            foreach (var rows in byStop)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    for (int pos = 0; pos < row.Items.Count; pos++)
                    {
                        var node = render.Nodes[row.Items[pos].Id];
                        if (r > 0)
                            node.Transitions[GraphDir.Up] = new Transition(VerticalTarget(row, rows[r - 1], pos));
                        if (r < rows.Count - 1)
                            node.Transitions[GraphDir.Down] = new Transition(VerticalTarget(row, rows[r + 1], pos));
                        if (pos > 0)
                            node.Transitions[GraphDir.Left] = new Transition(row.Items[pos - 1].Id);
                        if (pos < row.Items.Count - 1)
                            node.Transitions[GraphDir.Right] = new Transition(row.Items[pos + 1].Id);
                    }
                }
            }
        }

        // Where vertical navigation from position pos lands in the adjacent row: the same position when
        // the rows share a non-null key (column nav) and it exists there, else the first item.
        private static ControlId VerticalTarget(Row from, Row to, int pos)
        {
            if (from.Key != null && to.Key != null && Equals(from.Key, to.Key) && pos < to.Items.Count)
                return to.Items[pos].Id;
            return to.Items[0].Id;
        }
    }
}
