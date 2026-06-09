using System.Collections.Generic;
using WrathAccess.Input;

namespace WrathAccess.UI
{
    /// <summary>
    /// Windows-screen-reader-style navigation:
    /// - Tab / Shift-Tab traverse Panel tab-stops (descending through nested panels;
    ///   a List counts as one stop).
    /// - Arrows move within a List (or adjust a focused slider/dropdown).
    /// - Confirm activates the focused leaf.
    /// On entering a container, auto-focus its first leaf (recursively).
    /// </summary>
    public sealed class TraditionalNavigator : Navigator
    {
        protected override void BuildInitialFocus()
        {
            // An unfocused screen (exploration) starts with nothing focused — arrows bubble to the overlay,
            // and Tab enters the HUD. But if it has remembered focus (you were in the HUD and a sub-screen
            // covered it, e.g. the log), restore it instead of dropping back to exploration. Tabbing out of
            // the HUD clears that remembered focus, so a fresh entry stays unfocused.
            if (Screen != null && Screen.StartUnfocused && Screen.FocusedChild == null) return;

            // Restore remembered focus (each container's FocusedChild), falling back to the
            // first focusable. So returning to a screen (e.g. after closing a submenu) lands
            // back where you were, not at the top.
            Container node = Screen;
            while (node != null)
            {
                var child = RepresentativeChild(node);
                if (child == null) break;
                node.SetFocusedChild(child);
                Path.Add(child);
                if (node.Shape == ContainerShape.Tree) break; // landed on a top-level tree node; don't descend in
                node = child as Container;
            }
        }

        public override bool OnInputJustPressed(InputAction action)
        {
            switch (action.Key)
            {
                case "nav.up": return Arrow(NavDirection.Up);
                case "nav.down": return Arrow(NavDirection.Down);
                case "nav.left": return Arrow(NavDirection.Left);
                case "nav.right": return Arrow(NavDirection.Right);
                case "nav.next": return Tab(1);
                case "nav.prev": return Tab(-1);
                // Ctrl+Up/Down jump between FlowSheet regions; ignored (bubble) when not in a sheet.
                case "nav.regionPrev": return Current?.Parent is FlowSheet sp && RegionJump(sp, -1);
                case "nav.regionNext": return Current?.Parent is FlowSheet sn && RegionJump(sn, 1);
                case "nav.primary":
                    // Nothing focused (e.g. plain exploration) → don't consume; let Enter bubble to the
                    // global handler (Main → Scanner.InteractAtCursor = left-click the thing under the cursor).
                    // A cell with no action of its own falls through to its row's associated element (a table
                    // value cell → the row's radio/control); a cell that owns an action keeps priority.
                    if (Current == null) return false;
                    if (!TryActivate(Current)) TryActivate(Associated(Current));
                    return true;
                case "nav.secondary":
                    if (Current != null && !TryContext(Current)) TryContext(Associated(Current));
                    return true;
                case "nav.back":
                    // Screen-level back/close (e.g. Settings → Close). Consume only if the screen handles it.
                    return Screen != null && Screen.InvokeAction(ActionIds.Back);
                case "focus.tooltip":
                {
                    // In plain exploration (nothing focused) Space is the pause toggle, not a tooltip key —
                    // don't consume it; let it bubble to the global handler (Main.TogglePauseIfExploring).
                    // Once focused in the HUD (Current != null, still the ctx.ingame screen) Space reads the
                    // tooltip instead, so it must NOT bubble to pause.
                    if (Current == null) return false;
                    var tpl = Current.GetTooltipTemplate();
                    // A cell with no tooltip of its own falls back to its row's associated element (the
                    // control the row defers to), then to the row's tooltip — so Space works on any cell.
                    if (tpl == null) { var assoc = Associated(Current); if (assoc != null && assoc != Current) tpl = assoc.GetTooltipTemplate(); }
                    if (tpl == null && Current.Parent is Table grid) tpl = grid.RowTooltipForCell(Current);
                    if (tpl == null && Current.Parent is FlowSheet sheet) tpl = sheet.RowTooltipForCell(Current);
                    if (tpl != null) WrathAccess.Screens.TooltipScreen.Open(tpl);
                    else Speak(Loc.T("nav.no_tooltip"));
                    return true;
                }
                default:
                    return false; // not a nav key → bubble to globals
            }
        }

        private bool Arrow(NavDirection dir)
        {
            if (Current == null) return false;

            // A focused slider/dropdown advertises increase/decrease; Left/Right invoke them,
            // then announce just the new value (not the whole control/path). Takes priority over a
            // tree's collapse/expand so adjusting a setting still works inside the tree.
            if (dir == NavDirection.Left && Current.InvokeAction(ActionIds.Decrease)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }
            if (dir == NavDirection.Right && Current.InvokeAction(ActionIds.Increase)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }

            // Inside a horizontal sub-list (e.g. a keybinding Bar): Left/Right move between its items.
            if ((dir == NavDirection.Left || dir == NavDirection.Right)
                && Current.Parent != null && Current.Parent.Shape == ContainerShape.HorizontalList)
            {
                var snap = new List<UIElement>(Path);
                if (Move(dir)) { AnnounceDelta(snap, interrupt: true); return true; }
                // at the bar's edge → fall through so the tree can collapse/ascend
            }

            // Table (grid): 2-D cursor with Excel-style header+cell announce.
            if (Current.Parent is Table grid) return GridArrow(dir, grid);

            // FlowSheet (regioned grid): 2-D cursor across stacked regions, region/header announce.
            if (Current.Parent is FlowSheet sheet) return FlowArrow(dir, sheet);

            // Treeview region: Up/Down over expanded nodes (DFS); Right/Left expand/collapse/descend/ascend.
            var root = TreeRootOf(Current);
            if (root != null) return TreeArrow(dir, root);

            var snapshot = new List<UIElement>(Path);
            if (!Move(dir)) return false;
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // ---- Treeview navigation (ContainerShape.Tree) ----

        // The topmost Tree-shaped ancestor (the tab-stop the tree lives in), or null if not in a tree.
        private static Container TreeRootOf(UIElement e)
        {
            Container root = null;
            var c = (e as Container) ?? e?.Parent;
            while (c != null) { if (c.Shape == ContainerShape.Tree) root = c; c = c.Parent; }
            return root;
        }

        // The visible-list node representing the focused element: itself if it's a direct child of a
        // tree group (a group node or leaf), else the ancestor that is (e.g. the Bar holding a focused
        // keybinding slot).
        private static UIElement TreeNodeOf(UIElement e, Container root)
        {
            var cur = e;
            while (cur != null && cur != root)
            {
                if (cur.Parent != null && cur.Parent.Shape == ContainerShape.Tree) return cur;
                cur = cur.Parent;
            }
            return e;
        }

        // Nodes currently visible (DFS pre-order): a tree group contributes itself, then — only if
        // expanded — its children, recursively. A non-tree container (a Bar) is a single node.
        private static void CollectVisible(Container c, List<UIElement> outList)
        {
            foreach (var child in c.Children)
            {
                if (child.CanFocus) outList.Add(child);
                if (child is Container cc && cc.Shape == ContainerShape.Tree && cc.Expanded)
                    CollectVisible(cc, outList);
            }
        }

        // Focus a tree node; if it's a non-tree container (a Bar), descend to its first actionable leaf.
        private void FocusTreeNode(UIElement node)
        {
            BuildPathTo(node);
            if (node is Container c && c.Shape != ContainerShape.Tree)
            {
                var first = c.FirstFocusable();
                if (first != null) { c.SetFocusedChild(first); AppendWithDescend(first); }
            }
        }

        private bool TreeArrow(NavDirection dir, Container root)
        {
            var node = TreeNodeOf(Current, root);
            var group = node as Container;
            bool isGroup = group != null && group.Shape == ContainerShape.Tree && group.Expandable;

            if (dir == NavDirection.Right)
            {
                if (isGroup && !group.Expanded)
                {
                    group.Expand();
                    // A lazy drill-in can resolve to nothing (e.g. a skill whose glossary key has no
                    // entry). Don't leave a silent empty-expanded node — recollapse and say so.
                    if (group.Children.Count == 0) { group.Collapse(); Speak(Loc.T("nav.no_details"), interrupt: true); }
                    else Speak(node.GetFocusMessage().Resolve(), interrupt: true);
                    return true;
                }
                if (!(isGroup && group.Expanded)) return true; // leaf/empty → nothing to descend into
                dir = NavDirection.Down; // expanded group → descend = step to first child (next visible)
            }
            else if (dir == NavDirection.Left)
            {
                if (isGroup && group.Expanded) { group.Collapse(); Speak(node.GetFocusMessage().Resolve(), interrupt: true); return true; }
                var parent = node.Parent; // ascend to the enclosing group
                if (parent != null && parent != root && parent.CanFocus)
                {
                    var snap = new List<UIElement>(Path);
                    FocusTreeNode(parent);
                    AnnounceDelta(snap, interrupt: true);
                }
                return true;
            }

            var vis = new List<UIElement>();
            CollectVisible(root, vis);
            int idx = vis.IndexOf(node);
            if (idx < 0) return true;
            int ni = dir == NavDirection.Down ? idx + 1 : idx - 1;
            if (ni < 0 || ni >= vis.Count) return true; // at an end; consume
            var snapshot = new List<UIElement>(Path);
            FocusTreeNode(vis[ni]);
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // Move the cell cursor in a Table and announce Excel-style. Headers resolve by proximity
        // (nearest Column above / Row to the left / Group up column 0). We speak: the group when it
        // changes (crossing into a new block), the column header when the column changed, the row
        // header when the row changed, then the cell ("blank" if empty). A header cell just reads
        // itself (its own text is the header), plus the group if that changed.
        private bool GridArrow(NavDirection dir, Table table)
        {
            if (!table.TryCoords(Current, out int r, out int c)) return false;
            int nr = r, nc = c;
            switch (dir)
            {
                case NavDirection.Down: nr++; break;
                case NavDirection.Up: nr--; break;
                case NavDirection.Right: nc++; break;
                case NavDirection.Left: nc--; break;
            }
            var next = table.CellAt(nr, nc);
            if (next == null) return true; // edge → consume (no wrap)

            BuildPathTo(next);
            var parts = new List<string>();
            var role = table.RoleAt(nr, nc);

            var group = table.GroupText(nr, nc);
            if (group != table.GroupText(r, c) && !string.IsNullOrEmpty(group) && role != CellRole.Group)
                parts.Add(group); // entered a new block (the group cell itself already reads the name)

            if (role == CellRole.None) // data cell → tack on the axis header(s) that changed
            {
                if (nc != c) { var h = table.ColumnHeaderText(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
                if (nr != r) { var h = table.RowHeaderText(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
            }

            if (!string.IsNullOrEmpty(next.Role)) parts.Add(next.Role); // e.g. an actionable cell → "button"
            var cell = next.GetLabelText();
            parts.Add(string.IsNullOrWhiteSpace(cell) ? "blank" : cell);

            WrathAccess.UiSound.Hover(); // a focus move, like AnnounceDelta's interrupt path
            Speak(string.Join(", ", parts), interrupt: true);
            return true;
        }

        // Move the cell cursor in a FlowSheet. Left/Right step to the next visitable cell in the row;
        // Up/Down step a row, landing on the same column or — if it isn't visitable there (a narrower
        // region below) — the furthest-left visitable cell of that row. Announce: the region when it
        // changes (entry), the column header when the column changed, the row label when the row changed
        // (unless we're on the label cell itself), then the cell ("blank" if empty).
        private bool FlowArrow(NavDirection dir, FlowSheet sheet)
        {
            if (!sheet.TryCoords(Current, out int r, out int c)) return false;
            int nr = r, nc = c;
            UIElement next = null;
            switch (dir)
            {
                case NavDirection.Right:
                    for (int cc = c + 1; cc < sheet.ColCount; cc++)
                        if (sheet.Visitable(r, cc)) { nc = cc; next = sheet.CellAt(r, cc); break; }
                    break;
                case NavDirection.Left:
                    for (int cc = c - 1; cc >= 0; cc--)
                        if (sheet.Visitable(r, cc)) { nc = cc; next = sheet.CellAt(r, cc); break; }
                    break;
                case NavDirection.Down:
                case NavDirection.Up:
                    nr = dir == NavDirection.Down ? r + 1 : r - 1;
                    if (nr < 0 || nr >= sheet.RowCount) return true; // edge → consume
                    if (sheet.Visitable(nr, c)) { nc = c; next = sheet.CellAt(nr, c); }
                    else
                    {
                        int lc = sheet.LeftmostVisitable(nr);
                        if (lc < 0) return true;
                        nc = lc; next = sheet.CellAt(nr, lc);
                    }
                    break;
            }
            if (next == null) return true; // no neighbour that way → consume (no wrap)

            BuildPathTo(next);
            AnnounceFlowCell(sheet, r, c, nr, nc, next, regionEntry: false);
            return true;
        }

        // Ctrl+Up/Down: jump to the previous/next region's first landable cell.
        private bool RegionJump(FlowSheet sheet, int dir)
        {
            if (!sheet.TryCoords(Current, out int r, out int c)) return false;
            var target = sheet.StepRegion(sheet.RegionAt(r), dir);
            if (target == null) return true; // no region that way → consume
            int fr = sheet.RegionFirstRow(target);
            int fc = fr >= 0 ? sheet.LeftmostVisitable(fr) : -1;
            if (fc < 0) return true;
            var cell = sheet.CellAt(fr, fc);
            BuildPathTo(cell);
            AnnounceFlowCell(sheet, r, c, fr, fc, cell, regionEntry: true);
            return true;
        }

        // Run an element's primary action with its sound + in-place reannounce; returns whether it handled it.
        private bool TryActivate(UIElement e)
        {
            if (e == null || !e.InvokeAction(ActionIds.Activate)) return false;
            var sound = e.ActivateSound; // game plays this in the view handler we bypass
            if (sound.HasValue) WrathAccess.UiSound.Play(sound.Value);
            if (e.ReannounceOnActivate) Speak(e.GetStateMessage().Resolve(), interrupt: true);
            return true;
        }

        private bool TryContext(UIElement e)
        {
            if (e == null || !e.InvokeAction(ActionIds.Context)) return false;
            if (e.ReannounceOnContext) Speak(e.GetStateMessage().Resolve(), interrupt: true);
            return true;
        }

        // The interactive element a cell defers to — its row's associated element in a table — or null.
        private static UIElement Associated(UIElement cell)
            => cell?.Parent is FlowSheet fs ? fs.AssociatedElementForCell(cell) : null;

        private void AnnounceFlowCell(FlowSheet sheet, int r, int c, int nr, int nc, UIElement next, bool regionEntry)
        {
            var parts = new List<string>();
            var region = sheet.RegionAt(nr);
            bool entered = regionEntry || region != sheet.RegionAt(r);

            // A bar reads each cell as its own full focus message (so a radio button announces as one), with
            // the group label only on entry and only when it isn't just the control's own label repeated.
            if (region != null && region.ReadCellFocusMessage)
            {
                if (entered && !string.IsNullOrEmpty(region.Label) && region.Label != next.GetLabelText())
                    parts.Add(region.Label);
                var msg = next.GetFocusMessage().Resolve();
                parts.Add(string.IsNullOrWhiteSpace(msg) ? "blank" : msg);
                WrathAccess.UiSound.Hover();
                Speak(string.Join(", ", parts), interrupt: true);
                return;
            }

            // An associated-element table: on a ROW change (up/down, or region entry) announce the row's
            // element focus + the column data (see FlowSheet.ComposeAssociatedReadout). Same-row moves
            // (left/right) fall through to the normal header+value framing below, so column headers still
            // read on column change.
            if (nr != r)
            {
                var assoc = sheet.ComposeAssociatedReadout(next, withRegionLabel: entered);
                if (assoc != null) { WrathAccess.UiSound.Hover(); Speak(assoc, interrupt: true); return; }
            }

            if (entered && region != null && !string.IsNullOrEmpty(region.Label))
                parts.Add(region.Label + ", " + region.TypeName);
            if (nc != c) { var h = sheet.ColumnHeader(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
            if (nr != r && nc != 0) { var rl = sheet.RowLabel(nr); if (!string.IsNullOrEmpty(rl)) parts.Add(rl); }
            if (!string.IsNullOrEmpty(next.Role)) parts.Add(next.Role);
            var text = next.GetLabelText();
            parts.Add(string.IsNullOrWhiteSpace(text) ? "blank" : text);
            WrathAccess.UiSound.Hover(); // a focus move
            Speak(string.Join(", ", parts), interrupt: true);
        }

        private bool Tab(int step)
        {
            var stops = ComputeTabStops();
            if (stops.Count == 0) return false;
            // Current may be deeper than its tab-stop (a node inside a tree/list, where the stop is an
            // ancestor representative), so walk up to the nearest element that IS a stop — otherwise
            // IndexOf returns -1 and Tab wrongly jumps to the first stop.
            int idx = -1;
            for (var e = Current; e != null && idx < 0; e = e.Parent)
                idx = stops.IndexOf(e);

            // Unfocused (e.g. exploration): Tab enters the HUD at the first/last stop.
            if (idx < 0)
            {
                var snap = new List<UIElement>(Path);
                BuildPathTo(stops[step >= 0 ? 0 : stops.Count - 1]);
                AnnounceDelta(snap, interrupt: true);
                return true;
            }

            int ni = idx + step;
            if (ni < 0 || ni >= stops.Count)
            {
                // On an unfocused-capable screen, tabbing off either end drops back to the unfocused
                // state (exploration owns the arrows again) rather than sticking at the edge.
                if (Screen != null && Screen.StartUnfocused)
                {
                    Path.Clear();
                    Screen.SetFocusedChild(null); // truly unfocused → a later re-entry stays in exploration
                    if (!string.IsNullOrEmpty(Screen.ScreenName)) Speak(Screen.ScreenName, interrupt: true);
                    return true;
                }
                if (Screen != null && Screen.Wrap)
                    ni = ((ni % stops.Count) + stops.Count) % stops.Count; // wrap
                else
                    return true; // at the end; consume, no wrap
            }
            var snapshot = new List<UIElement>(Path);
            BuildPathTo(stops[ni]);
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // Arrow movement within list-shaped containers, spilling into a same-shape parent.
        private bool Move(NavDirection dir)
        {
            var movingFrom = Current;
            var container = movingFrom?.Parent;
            while (container != null)
            {
                var next = container.GetNeighbor(movingFrom, dir);
                if (next != null)
                {
                    int idx = Path.IndexOf(movingFrom);
                    if (idx >= 0) Path.RemoveRange(idx, Path.Count - idx);
                    AppendWithDescend(next);
                    container.SetFocusedChild(next);
                    return true;
                }
                var parent = container.Parent;
                if (parent != null && parent.Shape == container.Shape)
                {
                    movingFrom = container;
                    container = parent;
                    continue;
                }
                return false;
            }
            return false;
        }
    }
}
