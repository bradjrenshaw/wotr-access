using System;
using System.Collections.Generic;
using System.Text;

namespace WrathAccess.UI.Graph
{
    /// <summary>
    /// Composes the spoken line for a focus change by diffing the presentation hierarchies
    /// (<see cref="GraphNode.Context"/>) of the old and new focus: newly-entered levels are read
    /// outermost-first, then the landing control — "Difficulty settings, list, Normal, radio button,
    /// selected" — recursing as deep as the hierarchy goes. Moves within the same context (siblings) and
    /// moves outward (ascends) read just the control. This reproduces the old path-diff announcements
    /// from declarative metadata, with one composer for every way focus can change.
    /// </summary>
    public static class GraphAnnouncer
    {
        /// <summary>The line for landing on <paramref name="to"/> having come from <paramref name="from"/>
        /// (null = from nothing: the full chain reads). <paramref name="transitionLabel"/> is the crossed
        /// edge's spoken line, when it had one. Null when there is nothing to say.</summary>
        public static string Compose(GraphNode from, GraphNode to, string transitionLabel = null)
        {
            if (to == null) return null;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(transitionLabel)) parts.Add(transitionLabel);

            var oldCtx = from != null ? from.Context : GraphNode.EmptyContext;
            var newCtx = to.Context ?? (IReadOnlyList<ContextEntry>)GraphNode.EmptyContext;

            // Common prefix of the two chains — levels we were already inside stay silent.
            int i = 0;
            while (i < oldCtx.Count && i < newCtx.Count && SameLevel(oldCtx[i], newCtx[i])) i++;

            string leaf = LeafText(to);

            for (int j = i; j < newCtx.Count; j++)
            {
                var entry = newCtx[j];
                if (string.IsNullOrEmpty(entry.Label)) continue;
                // Dedupe: a container whose label just duplicates the next level down (or the control
                // itself — "a 'Game difficulty' section wrapping the 'Game difficulty' control").
                string next = j + 1 < newCtx.Count ? newCtx[j + 1].Label : leaf;
                if (!string.IsNullOrEmpty(next) && DuplicatesNext(entry.Label, next)) continue;
                parts.Add(string.IsNullOrEmpty(entry.Role) ? entry.Label : entry.Label + ", " + entry.Role);
            }

            if (!string.IsNullOrEmpty(leaf)) parts.Add(leaf);
            if (parts.Count == 0) return null;

            var sb = new StringBuilder();
            for (int p = 0; p < parts.Count; p++)
            {
                if (p > 0) sb.Append(", ");
                sb.Append(parts[p]);
            }
            return sb.ToString();
        }

        /// <summary>The full readout for a landing with no prior focus (screen entry, focus restore).</summary>
        public static string ComposeFull(GraphNode to) => Compose(null, to);

        /// <summary>Pluggable per-part filter — installed by the host to consult the user's announcement
        /// settings (per control type + per kind); null (tests, boot) = everything speaks. Returning false
        /// drops the part from readouts AND from the live watch.</summary>
        public static Func<ControlType, NodeAnnouncement, bool> PartFilter;

        /// <summary>
        /// A node's EFFECTIVE announcement parts: the control type's common parts (the role word) merged
        /// with the node's own — a node part overrides a common part of the same kind — sorted by the
        /// type's kind order (unknown/kindless parts append in declaration order), then filtered by the
        /// user's settings. This is the single list readouts and the live watch operate on.
        /// </summary>
        public static List<NodeAnnouncement> EffectiveAnnouncements(GraphNode node)
        {
            var result = new List<NodeAnnouncement>();
            var vt = node?.Vtable;
            if (vt == null) return result;
            var type = vt.ControlType;

            var common = type?.Common?.Invoke();
            if (common != null)
                foreach (var c in common)
                    if (c != null && !HasKind(vt.Announcements, c.Kind)) result.Add(c);
            if (vt.Announcements != null)
                foreach (var a in vt.Announcements)
                    if (a != null) result.Add(a);

            if (type?.Order != null && type.Order.Length > 0 && result.Count > 1)
            {
                // Stable: composite key = (kind's order index, declaration index) — List.Sort alone is
                // unstable and would scramble same-bucket (kindless) parts.
                var keyed = new List<KeyValuePair<long, NodeAnnouncement>>(result.Count);
                for (int i = 0; i < result.Count; i++)
                    keyed.Add(new KeyValuePair<long, NodeAnnouncement>(
                        (long)OrderIndex(type.Order, result[i].Kind) << 32 | (uint)i, result[i]));
                keyed.Sort((x, y) => x.Key.CompareTo(y.Key));
                result.Clear();
                foreach (var kv in keyed) result.Add(kv.Value);
            }

            if (PartFilter != null)
                result.RemoveAll(a => !PartFilter(type, a));
            return result;
        }

        private static bool HasKind(IReadOnlyList<NodeAnnouncement> anns, string kind)
        {
            if (anns == null || kind == null) return false;
            foreach (var a in anns)
                if (a != null && a.Kind == kind) return true;
            return false;
        }

        // Stable sort key: declared kinds by their order index; everything else after, keeping declaration
        // order (List.Sort is unstable, so unknown parts share one bucket — see the tie-break below).
        private static int OrderIndex(string[] order, string kind)
        {
            if (kind != null)
                for (int i = 0; i < order.Length; i++)
                    if (order[i] == kind) return i;
            return order.Length;
        }

        /// <summary>A node's own readout: its effective announcement parts, resolved live, non-empty ones
        /// joined. The first part is the control's label, so context dedupe's prefix check still applies.</summary>
        public static string LeafText(GraphNode node)
        {
            var anns = EffectiveAnnouncements(node);
            if (anns.Count == 0) return null;
            var sb = new StringBuilder();
            for (int i = 0; i < anns.Count; i++)
            {
                string t = null;
                if (anns[i]?.Text != null) t = anns[i].Text();
                if (string.IsNullOrEmpty(t)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static bool SameLevel(ContextEntry a, ContextEntry b)
            => string.Equals(a.Label, b.Label) && string.Equals(a.Role, b.Role);

        // The next part "starts as" this label: equal, or its first comma-separated segment is the label
        // (a control's focus message leads with its label: "Game difficulty, menu button").
        private static bool DuplicatesNext(string label, string next)
        {
            if (!next.StartsWith(label)) return false;
            return next.Length == label.Length || next[label.Length] == ',';
        }
    }
}
