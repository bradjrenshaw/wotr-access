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

            string leaf = to.Vtable?.Label?.Invoke();

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
