using System;
using System.Collections.Generic;

namespace WrathAccess.UI.Graph
{
    /// <summary>The four navigable directions between graph nodes (explicit edges). Tab-stop cycling and
    /// region jumps are OPERATIONS over node metadata (<see cref="GraphNode.StopKey"/> /
    /// <see cref="GraphNode.RegionKey"/>), not edges — they carry per-stop remembered positions, which a
    /// static edge can't express.</summary>
    public enum GraphDir
    {
        Up,
        Right,
        Down,
        Left,
    }

    /// <summary>One level of a node's presentation hierarchy — "Difficulty settings" (list), "Abilities"
    /// (table)… The announcer prefix-diffs these chains between focus moves so entering a container reads
    /// its labels outermost-first, exactly like the old path-diff announcements.</summary>
    public struct ContextEntry
    {
        public string Label;
        public string Role; // spoken after the label ("list", "table", "group"); null/empty = label only

        public ContextEntry(string label, string role = null) { Label = label; Role = role; }
    }

    /// <summary>
    /// The behaviors of a control, as data. <see cref="Label"/> is required (it produces the spoken focus
    /// announcement); the rest are optional — a null slot means the control doesn't have that behavior and
    /// the navigator speaks its "nothing there" feedback instead.
    /// </summary>
    public sealed class NodeVtable
    {
        /// <summary>Required. The control's full spoken focus announcement, resolved live.</summary>
        public Func<string> Label;

        /// <summary>Optional. Primary activation — the left-click equivalent (Enter).</summary>
        public Action OnActivate;

        /// <summary>Optional. Secondary activation — the right-click equivalent (Backspace).</summary>
        public Action OnSecondary;

        /// <summary>Optional. Read / open the control's tooltip (Space, F1). The action owns the whole
        /// behavior (speak, or open the drill-in tooltip reader), so the core stays game-agnostic.</summary>
        public Action OnTooltip;

        /// <summary>Optional. Horizontal value adjust (a slider): sign is -1 (decrease) / +1 (increase),
        /// large requests a coarse step. When set, left/right do NOT navigate.</summary>
        public Action<int, bool> OnAdjust;

        /// <summary>Optional. The control's state line, spoken in place after an activation that changes
        /// state (a toggle's new value) instead of repeating the whole label.</summary>
        public Func<string> StateText;

        /// <summary>Optional. The text type-ahead matches against; null = derive from <see cref="Label"/>.
        /// (A cell whose label is a bare number can search as its row's name, etc.)</summary>
        public Func<string> SearchText;

        /// <summary>If true, type-ahead never matches this control.</summary>
        public bool ExcludeFromSearch;
    }

    /// <summary>A directed edge to another node, with an optional spoken transition line (a "lane
    /// change" — e.g. crossing into a new column band). Kept as plain data; contextual announcements are
    /// composed from node metadata by the announcer, not per-edge closures (GC discipline).</summary>
    public sealed class Transition
    {
        public ControlId Destination;
        public string Label; // spoken only while crossing this edge; null = silent edge

        public Transition(ControlId destination, string label = null)
        {
            Destination = destination;
            Label = label;
        }
    }

    /// <summary>A control: identity, behaviors, directional transitions, and presentation metadata (its
    /// context chain, tab-stop and region membership).</summary>
    public sealed class GraphNode
    {
        public ControlId Id;
        public NodeVtable Vtable;
        public readonly Dictionary<GraphDir, Transition> Transitions = new Dictionary<GraphDir, Transition>();

        /// <summary>The presentation hierarchy this node sits in, outermost first. Never null (empty when
        /// the node is at screen level).</summary>
        public IReadOnlyList<ContextEntry> Context = EmptyContext;

        /// <summary>The Tab-stop this node belongs to. Nodes sharing a StopKey form one stop; Tab cycles
        /// stops in first-appearance order, landing on the stop's remembered position.</summary>
        public object StopKey;

        /// <summary>The region (within a stop) this node belongs to, or null. Ctrl+Up/Down jumps between
        /// regions in first-appearance order.</summary>
        public object RegionKey;

        internal static readonly ContextEntry[] EmptyContext = new ContextEntry[0];
    }

    /// <summary>
    /// One built snapshot of a graph: the nodes (keyed by structural identity), their order of
    /// declaration, and where focus starts when there is no prior position. Rebuilt per operation and
    /// thrown away — live state belongs in the node callbacks, not here.
    /// </summary>
    public sealed class GraphRender
    {
        public ControlId StartKey;
        public readonly Dictionary<ControlId, GraphNode> Nodes = new Dictionary<ControlId, GraphNode>();

        /// <summary>Declaration order — drives stop/region cycling and type-ahead scan order.</summary>
        public readonly List<GraphNode> Order = new List<GraphNode>();

        public GraphNode NodeAt(ControlId key)
        {
            if (key == null) return null;
            GraphNode n;
            return Nodes.TryGetValue(key, out n) ? n : null;
        }
    }

    /// <summary>
    /// The persistent cursor for a graph — the only thing that survives between renders. Holds where
    /// focus is, the last computed traversal order (for closest-survivor recovery), per-stop remembered
    /// positions (so Tab returns to where you were in a stop), and a one-shot move request.
    /// </summary>
    public sealed class GraphState
    {
        /// <summary>The focused control's id (carries its Reference for tier-1 recovery). Null until first render.</summary>
        public ControlId CurKey;

        /// <summary>The down-right total order from the previous render. Null on first render.</summary>
        public List<ControlId> KeyOrder;

        /// <summary>If set, focus jumps here on the next render when present (consumed either way).</summary>
        public ControlId NextSuggestedMove;

        /// <summary>Remembered position per Tab-stop: where Tab lands when cycling back into a stop.</summary>
        public readonly Dictionary<object, ControlId> StopMemory = new Dictionary<object, ControlId>();
    }
}
