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
            // Restore remembered focus (each container's FocusedChild), falling back to the
            // first focusable. So returning to a screen (e.g. after closing a submenu) lands
            // back where you were, not at the top.
            Container node = Screen;
            while (node != null)
            {
                var child = (node.FocusedChild != null && node.FocusedChild.CanFocus)
                    ? node.FocusedChild
                    : node.FirstFocusable();
                if (child == null) break;
                node.SetFocusedChild(child);
                Path.Add(child);
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
                case "nav.primary":
                    if (Current != null && Current.InvokeAction(ActionIds.Activate))
                    {
                        var sound = Current.ActivateSound; // game plays this in the view handler we bypass
                        if (sound.HasValue) WrathAccess.UiSound.Play(sound.Value);
                        if (Current.ReannounceOnActivate)
                            Speak(Current.GetStateMessage().Resolve(), interrupt: true); // just the changed state, not the whole control/path
                    }
                    return true;
                case "nav.secondary":
                    if (Current != null && Current.InvokeAction(ActionIds.Context) && Current.ReannounceOnContext)
                        Speak(Current.GetStateMessage().Resolve(), interrupt: true); // e.g. "not bound" after clearing
                    return true;
                case "nav.back":
                    // Screen-level back/close (e.g. Settings → Close). Consume only if the screen handles it.
                    return Screen != null && Screen.InvokeAction(ActionIds.Back);
                case "focus.tooltip":
                {
                    var tpl = Current?.GetTooltipTemplate();
                    if (tpl != null) WrathAccess.Screens.TooltipScreen.Open(tpl);
                    else Speak("No tooltip");
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
                if (isGroup && !group.Expanded) { group.Expand(); Speak(node.GetFocusMessage().Resolve(), interrupt: true); return true; }
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

        private bool Tab(int step)
        {
            var stops = ComputeTabStops();
            if (stops.Count == 0) return false;
            int idx = stops.IndexOf(Current);
            int ni = (idx < 0) ? 0 : idx + step;
            if (ni < 0 || ni >= stops.Count)
            {
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
