using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickGroundHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.View; // ObstacleAnalyzer (navmesh Area reachability)
using UnityEngine;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The scanner: a categorized, distance-sorted list of things in the current area, browsed with
    /// PageUp/Down (items) and Ctrl+PageUp/Down (categories). Home plants a virtual cursor on the
    /// selected item; K reads the cursor; Shift+K reads the whole party. Read-only for now — it browses
    /// and announces; movement/interaction come later.
    ///
    /// Active only while Focus Mode owns the keyboard AND the plain in-game context is on top (no
    /// dialogue/menu/window), so our keys never collide with the game's. Distances/bearings are relative
    /// to the main character. Lists rebuild on every action (cheap; always fresh), sorted by distance.
    /// </summary>
    internal static class Scanner
    {
        private static readonly Dictionary<ScanCategory, List<ScanItem>> _items =
            new Dictionary<ScanCategory, List<ScanItem>>();

        private static int _catIndex;   // index into ScanCategories.Order
        private static int _itemIndex;   // index into the current category's list
        private static bool _entered;    // first scanner key announces the current spot without moving
        private static bool _debugAll;   // debug (F11): list everything, ignoring the visibility filter

        private static bool Active =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        private static UnitEntityData Leader
        {
            get { var p = Game.Instance?.Player; return p != null ? p.MainCharacter.Value : null; }
        }

        private static Vector3 Reference => Geo.Live(Leader);

        // The scan list reads (and sorts) relative to the shared cursor when one is placed — so you can
        // "look around" from the cursor or a thing you planted it on (Home), or from wherever the tile
        // overlay last moved it. Falls back to the player when no cursor is set.
        private static Vector3 ScanFrom => Cursor.Has ? Cursor.Position.Value : Reference;

        // ---- input entry points (gated) ----
        public static void NextItem() { if (Active) StepItem(1); }
        public static void PrevItem() { if (Active) StepItem(-1); }
        public static void NextCategory() { if (Active) StepCategory(1); }
        public static void PrevCategory() { if (Active) StepCategory(-1); }
        public static void CursorToSelected() { if (Active) CommitCursor(); }
        public static void AnnounceCursor() { if (Active) SpeakCursor(); }
        public static void AnnounceParty() { if (Active) SpeakParty(); }
        public static void InteractSelected() { if (Active) DoInteract(); }
        public static void MoveToCursor() { if (Active) DoMoveToCursor(); }
        public static void ToggleDebugShowAll()
        {
            if (!Active) return;
            _debugAll = !_debugAll;
            Speak("Scanner debug: " + (_debugAll ? "showing all, including hidden" : "showing visible only"));
        }

        private static void Rebuild()
        {
            foreach (var c in ScanCategories.Order)
            {
                if (_items.TryGetValue(c, out var existing)) existing.Clear();
                else _items[c] = new List<ScanItem>();
            }

            foreach (var item in WorldModel.Items) Add(item); // Add filters to visible + buckets by category

            var refPos = ScanFrom;
            foreach (var list in _items.Values)
                list.Sort((a, b) => Geo.Distance(refPos, a.Position).CompareTo(Geo.Distance(refPos, b.Position)));
        }

        private static void Add(ScanItem item)
        {
            if (item == null) return;
            if (!_debugAll && !item.IsVisible) return; // debug (F11) lists hidden things too
            bool inAll = false;
            foreach (var cat in item.Categories)
            {
                if (_items.TryGetValue(cat, out var list)) list.Add(item);
                // "All" aggregates the real things — not scenery-only props, not the curated POI markers
                // (which duplicate entities). Added once, however many categories the item has.
                if (cat != ScanCategory.Scenery && cat != ScanCategory.PointsOfInterest) inAll = true;
            }
            if (inAll && _items.TryGetValue(ScanCategory.All, out var all)) all.Add(item);
        }

        private static List<ScanItem> Current =>
            _items.TryGetValue(ScanCategories.Order[_catIndex], out var l) ? l : null;

        private static ScanItem Selected
        {
            get { var l = Current; return (l != null && _itemIndex >= 0 && _itemIndex < l.Count) ? l[_itemIndex] : null; }
        }

        private static void StepCategory(int dir)
        {
            Rebuild();
            int n = ScanCategories.Order.Length;
            if (_entered) _catIndex = ((_catIndex + dir) % n + n) % n;
            _entered = true;
            _itemIndex = 0;
            AnnounceCategory();
        }

        private static void StepItem(int dir)
        {
            Rebuild();
            var list = Current;
            if (list == null || list.Count == 0)
            {
                _entered = true;
                Speak(ScanCategories.Label(ScanCategories.Order[_catIndex]) + ", empty");
                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, list.Count - 1);
            if (_entered) _itemIndex = ((_itemIndex + dir) % list.Count + list.Count) % list.Count;
            _entered = true;
            AnnounceItem(list);
        }

        private static void AnnounceCategory()
        {
            var cat = ScanCategories.Order[_catIndex];
            var list = Current;
            int count = list != null ? list.Count : 0;
            var msg = ScanCategories.Label(cat) + ", " + count + (count == 1 ? " item" : " items");
            if (count > 0) { _itemIndex = Mathf.Clamp(_itemIndex, 0, count - 1); msg += ". " + ItemLine(list); }
            Speak(msg);
        }

        private static void AnnounceItem(List<ScanItem> list) => Speak(ItemLine(list));

        private static string ItemLine(List<ScanItem> list)
        {
            var item = list[_itemIndex];
            var tag = (_debugAll && !item.IsVisible) ? " (hidden)" : ""; // flag fogged items in debug mode
            return item.Describe(ScanFrom) + tag + ", " + (_itemIndex + 1) + " of " + list.Count;
        }

        // Cursor/interact act on the item you last navigated to (the cached selection) — no rebuild,
        // so they don't re-sort the list out from under you between hearing an item and acting on it.
        private static void CommitCursor()
        {
            var sel = Selected;
            if (sel == null) { Speak("No item selected"); return; }
            Cursor.Set(sel.Position); // the shared cursor — overlays move this same point
            Speak("Cursor on " + (string.IsNullOrEmpty(sel.Name) ? "item" : sel.Name) + ", " + Geo.Raw(sel.Position));
        }

        private static void DoInteract()
        {
            var sel = Selected;
            if (sel == null) { Speak("No item selected"); return; }
            var name = string.IsNullOrEmpty(sel.Name) ? "that" : sel.Name;
            var mc = Game.Instance?.Player?.MainCharacter.Value;
            if (mc != null)
            {
                // Interact approaches the thing adjacently (it doesn't path onto the thing's exact point),
                // so we don't require its centre to be on-mesh. A closed door sits in its own navmesh cut,
                // so its centre can snap to the far side — re-test a point nudged toward the actor, which
                // lands on our side, so we don't wrongly block walking up to open it.
                var from = Geo.Live(mc);
                bool reachable = SameArea(from, sel.Position)
                                 || SameArea(from, Vector3.MoveTowards(sel.Position, from, 2f));
                if (!reachable) { Speak("Can't reach " + name + ", no path"); return; }
            }
            EnsureSelection();
            if (sel.Interact())
                Speak("Interacting with " + (string.IsNullOrEmpty(sel.Name) ? "item" : sel.Name));
            else
                Speak("Can't interact with " + name);
        }

        // Walk to the shared cursor — the point planted by Home or moved by the tile-view overlay. Routes
        // through the game's own MoveSelectedUnitsToPoint, so the selection decides the behaviour exactly
        // like a mouse click: one member selected → only they go; the whole party → everyone into the set
        // formation at the target ([[wotr-access-party-selection]]). Selection is set by the Ctrl+A /
        // Ctrl+1..6 actions and read here synchronously. A move issued while paused queues and walks on
        // unpause (Space). Reachability is checked from the lead selected unit.
        private static void DoMoveToCursor()
        {
            if (!Cursor.Has) { Speak("No cursor set"); return; }
            var dest = Cursor.Position.Value;
            if (!OnNavmesh(dest)) { Speak("Can't move there, not walkable"); return; }

            EnsureSelection(); // default to the whole party if nothing's selected yet
            var refUnit = Game.Instance?.SelectionCharacter?.FirstSelectedUnit
                          ?? Game.Instance?.Player?.MainCharacter.Value;
            if (refUnit == null) { Speak("No character to move"); return; }
            if (!SameArea(Geo.Live(refUnit), dest)) { Speak("Can't reach the cursor, no path"); return; }

            ClickGroundHandler.MoveSelectedUnitsToPoint(dest);
            Speak("Moving to cursor");
        }

        private const uint NoArea = 999999u; // ObstacleAnalyzer.GetArea's sentinel when no node is near

        // Two world points are mutually walkable iff their nearest navmesh nodes share an Area (connected
        // component) — the game's own cross-Area move block ([[wotr-exploration-world-model]]): a closed
        // door that's the only route splits the Areas, so this is "no path" until it's opened. Because
        // reachable ⇒ same Area, a DIFFERENT id means genuinely unreachable, so we never block a target you
        // could actually reach. Unknown (off-mesh → NoArea) is treated as same so a snap we can't classify
        // doesn't block; callers that must not path onto off-mesh points gate on OnNavmesh first.
        private static bool SameArea(Vector3 a, Vector3 b)
        {
            uint ar = ObstacleAnalyzer.GetArea(a), br = ObstacleAnalyzer.GetArea(b);
            return ar == NoArea || br == NoArea || ar == br;
        }

        // Is this point actually on walkable ground? We refuse to path to off-navmesh points: the game
        // would silently clamp to the nearest walkable spot, and a blind player can't see the marker
        // showing where it landed. (Same 0.35 m tolerance the tile view uses to call a tile walkable.)
        private static bool OnNavmesh(Vector3 p) => NavmeshProbe.Sample(p.x, p.z, p.y).OnNavmesh;

        // The move/interaction logic acts on the player's current selection; keyboard nav may have none
        // (the player hasn't pressed a selection key yet), so default to the whole party — the game's own
        // default. SelectAll goes through MultiSelect (silent, no selection voice) and writes SelectedUnits
        // synchronously, so the move that follows sees it immediately.
        private static void EnsureSelection()
        {
            var sc = Game.Instance?.SelectionCharacter;
            if (sc == null) return;
            if (sc.SelectedUnits != null && sc.SelectedUnits.Count > 0) return;
            Game.Instance.UI?.SelectionManager?.SelectAll();
        }

        private static void SpeakCursor()
        {
            if (!Cursor.Has) { Speak("No cursor set"); return; }
            Speak("Cursor, " + Geo.Relative(Reference, Cursor.Position.Value));
        }

        private static void SpeakParty()
        {
            var player = Game.Instance?.Player;
            var members = player != null ? player.PartyAndPets : null;
            if (members == null || members.Count == 0) { Speak("No party"); return; }
            var refPos = Reference;
            var parts = new List<string>();
            foreach (var m in members)
                if (m != null) parts.Add(m.CharacterName + ", " + Geo.Relative(refPos, Geo.Live(m)));
            Speak("Party: " + string.Join("; ", parts.ToArray()));
        }

        private static void Speak(string msg) { if (!string.IsNullOrEmpty(msg)) Tts.Speak(msg, interrupt: true); }
    }
}
