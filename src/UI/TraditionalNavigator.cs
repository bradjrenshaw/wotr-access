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
            // then announce just the new value (not the whole control/path).
            if (dir == NavDirection.Left && Current.InvokeAction(ActionIds.Decrease)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }
            if (dir == NavDirection.Right && Current.InvokeAction(ActionIds.Increase)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }

            var snapshot = new List<UIElement>(Path);
            if (!Move(dir)) return false;
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
