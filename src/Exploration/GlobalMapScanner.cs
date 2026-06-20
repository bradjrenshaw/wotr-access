using System.Collections.Generic;
using System.Linq;
using Kingmaker.Globalmap.Blueprints; // GlobalMapPointType
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map REVIEW CURSOR — the analogue of the in-area Scanner (whose selection IS the review
    /// cursor). One selected point, moved by EITHER the categorised browse (PageUp/Down items,
    /// Ctrl+PageUp/Down categories: Everything / Locations / Junctions) OR the single-key cycles (b = all
    /// points, m = direct connections, n = all reachable locations). <b>I</b> interacts with it (travel /
    /// enter). Isolated from the in-area Scanner (no navmesh / rooms / fog); reads GlobalMapModel live.
    /// The WASD MOVEMENT cursor + Enter (act on the movement cursor) are a separate system — next increment.
    /// </summary>
    internal static class GlobalMapScanner
    {
        private enum Cat { Everything, Locations, Junctions }
        private static readonly Cat[] Cats = { Cat.Everything, Cat.Locations, Cat.Junctions };

        private static GlobalMapPointView _selected; // THE review cursor — shared by every move below
        private static int _catIndex;                // which category PageUp/Down browses

        public static void Reset() { _selected = null; _catIndex = 0; }

        // ---- the shared cursor move: find _selected in `list`, step, set it, announce ----
        private static void Move(List<GlobalMapPointView> list, int dir, string emptyMsg)
        {
            if (list.Count == 0) { Tts.Speak(emptyMsg); return; }
            int i = _selected != null ? list.IndexOf(_selected) : -1;
            i = i < 0 ? (dir > 0 ? 0 : list.Count - 1) : Mathf.Clamp(i + dir, 0, list.Count - 1);
            _selected = list[i];
            Announce(i, list.Count);
        }

        private static void Announce(int i, int count)
            => Tts.Speak(GlobalMapActions.Label(_selected) + ", " + Loc.T("nav.position", new { index = i + 1, count }));

        // ---- categorised scanner browse ----
        private static List<GlobalMapPointView> CategoryList(Cat c)
        {
            IEnumerable<GlobalMapPointView> src =
                c == Cat.Locations ? GlobalMapModel.Locations
                : c == Cat.Junctions ? GlobalMapModel.Junctions
                : GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions);
            return Sorted(src);
        }

        private static string CatLabel(Cat c)
            => Loc.T(c == Cat.Locations ? "worldmap.cat_locations" : c == Cat.Junctions ? "worldmap.cat_junctions" : "worldmap.cat_everything");

        public static void NextItem() => Move(CategoryList(Cats[_catIndex]), +1, Empty(Cats[_catIndex]));
        public static void PrevItem() => Move(CategoryList(Cats[_catIndex]), -1, Empty(Cats[_catIndex]));
        private static string Empty(Cat c) => Loc.T("worldmap.scan_empty", new { cat = CatLabel(c) });

        public static void NextCategory() => StepCat(+1);
        public static void PrevCategory() => StepCat(-1);
        private static void StepCat(int dir)
        {
            _catIndex = ((_catIndex + dir) % Cats.Length + Cats.Length) % Cats.Length;
            Tts.Speak(CatLabel(Cats[_catIndex]));
            var list = CategoryList(Cats[_catIndex]);
            if (list.Count == 0) { Tts.Speak(Empty(Cats[_catIndex])); return; }
            _selected = list[0]; // land on the nearest in the new category
            Announce(0, list.Count);
        }

        // ---- single-key review cycles (b / m / n) — same cursor ----
        public static void AllNext() => Move(CategoryList(Cat.Everything), +1, Loc.T("worldmap.no_points"));
        public static void AllPrev() => Move(CategoryList(Cat.Everything), -1, Loc.T("worldmap.no_points"));
        public static void ConnectedNext() => Move(ConnectedPoints(), +1, Loc.T("worldmap.no_connections"));
        public static void ConnectedPrev() => Move(ConnectedPoints(), -1, Loc.T("worldmap.no_connections"));
        public static void ReachableNext() => Move(ReachableLocations(), +1, Loc.T("worldmap.no_reachable"));
        public static void ReachablePrev() => Move(ReachableLocations(), -1, Loc.T("worldmap.no_reachable"));

        // ---- interact (i): act on the review cursor ----
        public static void Interact()
        {
            if (_selected == null) { Tts.Speak(Loc.T("worldmap.scan_none")); return; }
            GlobalMapActions.Go(_selected);
        }

        // ---- list builders ----
        private static List<GlobalMapPointView> Sorted(IEnumerable<GlobalMapPointView> src)
        {
            var from = GlobalMapModel.TravelerPos;
            return src.OrderBy(p => Geo.Distance(from, p.transform.position)).ToList();
        }

        // m: the revealed points the party's current location directly connects to (one hop).
        private static List<GlobalMapPointView> ConnectedPoints()
        {
            var cur = GlobalMapModel.CurrentLocationView;
            return cur == null ? new List<GlobalMapPointView>() : Sorted(Neighbors(cur).Distinct());
        }

        private static IEnumerable<GlobalMapPointView> Neighbors(GlobalMapPointView p)
        {
            if (p.Edges == null) yield break;
            foreach (var e in p.Edges)
            {
                var other = e.Point1 == p ? e.Point2 : e.Point1;
                if (other != null && other.State != null && other.State.IsRevealed) yield return other;
            }
        }

        // n: every LOCATION reachable from the current node through the road network — a BFS over revealed,
        // non-locked edges (traversing junctions), nearest-first. Excludes the node you're standing on.
        private static List<GlobalMapPointView> ReachableLocations()
        {
            var start = GlobalMapModel.CurrentLocationView;
            if (start == null) return new List<GlobalMapPointView>();
            var visited = new HashSet<GlobalMapPointView> { start };
            var queue = new Queue<GlobalMapPointView>();
            queue.Enqueue(start);
            var result = new List<GlobalMapPointView>();
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (p.Edges == null) continue;
                foreach (var e in p.Edges)
                {
                    if (e.IsLocked) continue; // a closed road = the far side isn't reachable
                    var other = e.Point1 == p ? e.Point2 : e.Point1;
                    if (other == null || other.State == null || !other.State.IsRevealed) continue;
                    if (!visited.Add(other)) continue;
                    queue.Enqueue(other); // traverse on (through junctions) ...
                    if (other.Blueprint != null && other.Blueprint.Type == GlobalMapPointType.Location)
                        result.Add(other); // ... but only collect enterable locations
                }
            }
            return Sorted(result);
        }
    }
}
