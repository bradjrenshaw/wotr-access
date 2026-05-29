using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickGroundHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // LocalMapModel
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
        private static Vector3? _cursor;
        private static bool _entered;    // first scanner key announces the current spot without moving

        private static bool Active =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        private static UnitEntityData Leader
        {
            get { var p = Game.Instance?.Player; return p != null ? p.MainCharacter.Value : null; }
        }

        private static Vector3 Reference => Geo.Live(Leader);

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

        private static void Rebuild()
        {
            foreach (var c in ScanCategories.Order)
            {
                if (_items.TryGetValue(c, out var existing)) existing.Clear();
                else _items[c] = new List<ScanItem>();
            }

            var game = Game.Instance;
            var state = game != null ? game.State : null;
            if (state != null)
            {
                foreach (var u in state.Units) Add(new ProxyUnit(u));
                foreach (var o in state.MapObjects) Add(new ProxyMapObject(o));
            }
            foreach (var m in LocalMapModel.Markers)
            {
                if (m == null) continue;
                try { if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue; } catch { }
                Add(new ProxyMarker(m));
            }

            var refPos = Reference;
            foreach (var list in _items.Values)
                list.Sort((a, b) => Geo.Distance(refPos, a.Position).CompareTo(Geo.Distance(refPos, b.Position)));
        }

        private static void Add(ScanItem item)
        {
            if (item == null || !item.IsVisible) return;
            foreach (var cat in item.Categories)
                if (_items.TryGetValue(cat, out var list)) list.Add(item);
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

        private static string ItemLine(List<ScanItem> list) =>
            list[_itemIndex].Describe(Reference) + ", " + (_itemIndex + 1) + " of " + list.Count;

        // Cursor/interact act on the item you last navigated to (the cached selection) — no rebuild,
        // so they don't re-sort the list out from under you between hearing an item and acting on it.
        private static void CommitCursor()
        {
            var sel = Selected;
            if (sel == null) { Speak("No item selected"); return; }
            _cursor = sel.Position;
            Speak("Cursor on " + (string.IsNullOrEmpty(sel.Name) ? "item" : sel.Name) + ", " + Geo.Raw(sel.Position));
        }

        private static void DoInteract()
        {
            var sel = Selected;
            if (sel == null) { Speak("No item selected"); return; }
            EnsureSelection();
            if (sel.Interact())
                Speak("Interacting with " + (string.IsNullOrEmpty(sel.Name) ? "item" : sel.Name));
            else
                Speak("Can't interact with " + (string.IsNullOrEmpty(sel.Name) ? "that" : sel.Name));
        }

        // Walk the selected units to the virtual cursor — mirrors right-click-to-move (the game clamps
        // an unreachable point to the nearest accessible one). Cursor is planted with Home.
        private static void DoMoveToCursor()
        {
            if (_cursor == null) { Speak("No cursor set"); return; }
            EnsureSelection();
            ClickGroundHandler.MoveSelectedUnitsToPoint(_cursor.Value);
            Speak("Moving to cursor");
        }

        // The interaction handlers act on the player's current selection; keyboard nav may have none, so
        // make sure at least the main character is selected (a mouse player always has a selection).
        private static void EnsureSelection()
        {
            var sc = Game.Instance?.SelectionCharacter;
            if (sc == null) return;
            var units = sc.SelectedUnits;
            if (units != null && units.Count > 0) return;
            var player = Game.Instance.Player;
            var mc = player != null ? player.MainCharacter.Value : null;
            if (mc != null) sc.SetSelected(mc);
        }

        private static void SpeakCursor()
        {
            if (_cursor == null) { Speak("No cursor set"); return; }
            Speak("Cursor, " + Geo.Relative(Reference, _cursor.Value));
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
