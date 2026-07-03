using System;
using System.Collections.Generic;

namespace WrathAccess.UI.Graph
{
    /// <summary>The outcome of a navigation operation, for the caller (navigator) to announce. The core
    /// never speaks — it returns what happened.</summary>
    public struct MoveResult
    {
        public bool Moved;              // focus actually changed nodes
        public GraphNode From;          // node before the operation (null on first landing)
        public GraphNode To;            // node after (== From when at an edge; null when the graph is empty)
        public string TransitionLabel;  // the crossed edge's spoken line, when it had one
    }

    /// <summary>
    /// The navigation engine: a directed graph of controls rebuilt from a render callback on each
    /// operation, with focus persisting in an external <see cref="GraphState"/>. Ported from Tanglebeep
    /// (with permission), itself from Factorio Access's key-graph.lua. Two invariants carry over:
    ///
    /// <para><b>Down-right total order</b> (<see cref="ComputeOrder"/>): from the start node, go right
    /// until stuck, queueing each down — visits a planar UI in reading order. Nodes down-right can't reach
    /// (later Tab-stops) are appended in declaration order, keeping the order total.</para>
    ///
    /// <para><b>Focus recovery on rebuild</b> (<see cref="Reconcile"/>): if the focused control vanished,
    /// land on the nearest survivor rather than jumping to the start — following the backing object that
    /// moved (tier 1) or the logical control whose backing object was rebuilt (tier 2) first.</para>
    ///
    /// Extensions over the original: Tab-stop cycling and region jumps as operations over node metadata
    /// (with per-stop remembered positions), and per-node secondary/tooltip/adjust behaviors.
    /// </summary>
    public sealed class KeyGraph
    {
        private readonly Func<GraphRender> _renderCallback;
        private readonly GraphState _state;
        private GraphRender _current;

        public KeyGraph(Func<GraphRender> renderCallback, GraphState state)
        {
            _renderCallback = renderCallback;
            _state = state;
        }

        public GraphState State => _state;

        /// <summary>The most recently built render, or null if not yet rendered / empty.</summary>
        public GraphRender Current => _current;

        /// <summary>The focused node in the current render, or null.</summary>
        public GraphNode CurrentNode => _current?.NodeAt(_state.CurKey);

        /// <summary>Rebuild the render and reconcile focus into it. False when the callback produced
        /// nothing (the caller should treat the graph as closed/empty).</summary>
        public bool Rerender()
        {
            _current = _renderCallback();
            if (_current == null || _current.Nodes.Count == 0)
            {
                _current = null;
                return false;
            }
            Reconcile(_current, _state);
            return true;
        }

        /// <summary>
        /// Move focus from the cached <see cref="GraphState.CurKey"/> to a valid control in
        /// <paramref name="render"/>, then recompute the traversal order.
        /// </summary>
        public static void Reconcile(GraphRender render, GraphState state)
        {
            // Honor a pending suggested move first, if its target still exists (consumed either way).
            if (state.NextSuggestedMove != null)
            {
                if (render.Nodes.ContainsKey(state.NextSuggestedMove))
                    state.CurKey = render.Nodes[state.NextSuggestedMove].Id;
                state.NextSuggestedMove = null;
            }

            ControlId old = state.CurKey;
            ControlId resolved = null;

            if (old != null)
            {
                // Tier 1: the same backing object, even if its structural key changed (it moved).
                if (old.Reference != null)
                {
                    foreach (var kv in render.Nodes)
                        if (kv.Value.Id.ReferenceMatches(old.Reference)) { resolved = kv.Value.Id; break; }
                }

                // Tier 2: the same structural key, even if the backing object was rebuilt.
                if (resolved == null)
                {
                    GraphNode structural;
                    if (render.Nodes.TryGetValue(old, out structural)) resolved = structural.Id;
                }

                // Fallback: nearest survivor walking the previous order backward.
                if (resolved == null && state.KeyOrder != null)
                {
                    int oldIndex = IndexOf(state.KeyOrder, old);
                    if (oldIndex >= 0)
                        for (int i = oldIndex; i >= 0; i--)
                        {
                            GraphNode survivor;
                            if (render.Nodes.TryGetValue(state.KeyOrder[i], out survivor))
                            {
                                resolved = survivor.Id;
                                break;
                            }
                        }
                }
            }

            // Nothing matched (or first render): the start node.
            if (resolved == null)
                resolved = render.Nodes.ContainsKey(render.StartKey) ? render.Nodes[render.StartKey].Id : render.StartKey;

            state.CurKey = resolved;
            RememberStop(render, state, resolved);
            state.KeyOrder = ComputeOrder(render);
        }

        /// <summary>
        /// The down-right total order: go right until stuck (recording each node), queue every down for a
        /// later pass, repeat — then append any node the walk never reached (e.g. later Tab-stops, which
        /// have no cross-stop edges) in declaration order, so the order is total.
        /// </summary>
        public static List<ControlId> ComputeOrder(GraphRender render)
        {
            var order = new List<ControlId>();
            var seen = new HashSet<ControlId>();
            var downFringe = new List<ControlId> { render.StartKey };

            int i = 0;
            while (i < downFringe.Count)
            {
                ControlId k = downFringe[i];
                while (!seen.Contains(k))
                {
                    seen.Add(k);
                    order.Add(k);

                    GraphNode n;
                    if (!render.Nodes.TryGetValue(k, out n)) break;

                    Transition d, t;
                    if (n.Transitions.TryGetValue(GraphDir.Down, out d) && d != null)
                        downFringe.Add(d.Destination);
                    if (!n.Transitions.TryGetValue(GraphDir.Right, out t) || t == null) break;
                    k = t.Destination;
                }
                i++;
            }

            foreach (var node in render.Order)
                if (seen.Add(node.Id)) order.Add(node.Id);

            return order;
        }

        private static int IndexOf(List<ControlId> order, ControlId key)
        {
            for (int i = 0; i < order.Count; i++)
                if (order[i].Equals(key)) return i;
            return -1;
        }

        private static void RememberStop(GraphRender render, GraphState state, ControlId key)
        {
            var node = render.NodeAt(key);
            if (node?.StopKey != null) state.StopMemory[node.StopKey] = key;
        }

        private void SetCurrent(GraphNode node)
        {
            _state.CurKey = node.Id;
            if (node.StopKey != null) _state.StopMemory[node.StopKey] = node.Id;
        }

        // ---- navigation operations ----

        /// <summary>One step in <paramref name="dir"/>. Not moved (at an edge / empty) → To == From.</summary>
        public MoveResult Move(GraphDir dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            Transition t;
            node.Transitions.TryGetValue(dir, out t);
            var dest = t != null ? _current.NodeAt(t.Destination) : null;
            if (dest == null || dest == node) return result;

            SetCurrent(dest);
            result.To = dest;
            result.Moved = true;
            result.TransitionLabel = t.Label;
            return result;
        }

        /// <summary>As far as possible in <paramref name="dir"/> (Home/End within a row or column).</summary>
        public MoveResult MoveToEdge(GraphDir dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            var cur = node;
            while (true)
            {
                Transition t;
                if (!cur.Transitions.TryGetValue(dir, out t) || t == null) break;
                var next = _current.NodeAt(t.Destination);
                if (next == null || next == cur) break;
                cur = next;
            }

            if (cur != node)
            {
                SetCurrent(cur);
                result.To = cur;
                result.Moved = true;
            }
            return result;
        }

        /// <summary>Cycle to the next/previous Tab-stop (declaration order), landing on the stop's
        /// remembered position (else its first node). <paramref name="wrap"/> continues past the ends;
        /// without it, at the last/first stop the result is not-moved (the caller may blur instead).</summary>
        public MoveResult MoveStop(int dir, bool wrap)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            var stops = StopOrder();
            if (stops.Count <= 1) return result;

            int idx = stops.IndexOf(node.StopKey);
            if (idx < 0) return result;
            int ni = idx + dir;
            if (wrap) ni = ((ni % stops.Count) + stops.Count) % stops.Count;
            if (ni < 0 || ni >= stops.Count || ni == idx) return result;

            var dest = StopLanding(stops[ni]);
            if (dest == null) return result;

            SetCurrent(dest);
            result.To = dest;
            result.Moved = true;
            return result;
        }

        /// <summary>Jump to the next/previous region within the current stop (declaration order), landing
        /// on the region's first node.</summary>
        public MoveResult MoveRegion(int dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null || node.RegionKey == null) return result;

            var regions = new List<object>();
            foreach (var n in _current.Order)
                if (Equals(n.StopKey, node.StopKey) && n.RegionKey != null && !regions.Contains(n.RegionKey))
                    regions.Add(n.RegionKey);

            int idx = regions.IndexOf(node.RegionKey);
            int ni = idx + dir;
            if (idx < 0 || ni < 0 || ni >= regions.Count) return result;

            foreach (var n in _current.Order)
                if (Equals(n.StopKey, node.StopKey) && Equals(n.RegionKey, regions[ni]))
                {
                    SetCurrent(n);
                    result.To = n;
                    result.Moved = true;
                    return result;
                }
            return result;
        }

        /// <summary>Move focus to a specific control (a node just revealed, a screen's chosen landing).
        /// False when it isn't in the render.</summary>
        public bool Focus(ControlId id)
        {
            if (id == null || !Rerender()) return false;
            var node = _current.NodeAt(id);
            if (node == null) return false;
            SetCurrent(node);
            return true;
        }

        /// <summary>Tier-1 focus sync from the game: if a node's backing object is
        /// <paramref name="reference"/>, move focus there. True if focus changed nodes.</summary>
        public bool FocusByReference(object reference)
        {
            if (reference == null || _current == null) return false;
            foreach (var kv in _current.Nodes)
                if (kv.Value.Id.ReferenceMatches(reference))
                {
                    bool changed = _state.CurKey == null || !_state.CurKey.Equals(kv.Value.Id);
                    SetCurrent(kv.Value);
                    return changed;
                }
            return false;
        }

        private List<object> StopOrder()
        {
            var stops = new List<object>();
            foreach (var n in _current.Order)
                if (n.StopKey != null && !stops.Contains(n.StopKey)) stops.Add(n.StopKey);
            return stops;
        }

        private GraphNode StopLanding(object stopKey)
        {
            ControlId remembered;
            if (_state.StopMemory.TryGetValue(stopKey, out remembered))
            {
                var node = _current.NodeAt(remembered);
                if (node != null && Equals(node.StopKey, stopKey)) return node;
            }
            foreach (var n in _current.Order)
                if (Equals(n.StopKey, stopKey)) return n;
            return null;
        }

        // ---- behavior invokers (the caller announces fallbacks / state) ----

        /// <summary>Run the focused control's primary activation. False = it has none.</summary>
        public bool Activate()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnActivate == null) return false;
            node.Vtable.OnActivate();
            return true;
        }

        /// <summary>Run the focused control's secondary activation. False = it has none.</summary>
        public bool Secondary()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnSecondary == null) return false;
            node.Vtable.OnSecondary();
            return true;
        }

        /// <summary>Run the focused control's tooltip behavior. False = it has none.</summary>
        public bool Tooltip()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnTooltip == null) return false;
            node.Vtable.OnTooltip();
            return true;
        }

        /// <summary>If the focused control adjusts horizontally (a slider), adjust and return true;
        /// false = the caller should navigate instead.</summary>
        public bool TryAdjust(int sign, bool large)
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnAdjust == null) return false;
            node.Vtable.OnAdjust(sign, large);
            return true;
        }
    }
}
