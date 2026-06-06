using System.Collections.Generic;
using WrathAccess.Input;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// Owns navigation: consumes input, holds the focus path (within the screen,
    /// excluding the screen root itself — the screen name is announced separately),
    /// and centralizes focus-path diffing + announcement. Pluggable by user
    /// preference (TraditionalNavigator, TreeNavigator, …).
    ///
    /// Critical: focus mutations are silent. A navigation step snapshots the path,
    /// mutates it (including any recursive auto-descend), then announces the diff
    /// ONCE — never on each intermediate SetFocus.
    /// </summary>
    public abstract class Navigator
    {
        protected readonly List<UIElement> Path = new List<UIElement>();
        protected Screen Screen { get; private set; }

        public UIElement Current => Path.Count > 0 ? Path[Path.Count - 1] : null;

        /// <summary>Bind to a screen and set initial focus (silently). Subclass decides how.</summary>
        public void Attach(Screen screen)
        {
            Screen = screen;
            Path.Clear();
            if (screen != null) BuildInitialFocus();
        }

        protected abstract void BuildInitialFocus();

        public abstract bool OnInputJustPressed(InputAction action);
        public virtual bool OnInputHeld(InputAction action) => false;
        public virtual bool OnInputReleased(InputAction action) => false;

        private static readonly List<UIElement> EmptyPath = new List<UIElement>();

        /// <summary>
        /// Announce the full focus path (e.g. when focus mode engages or the screen
        /// changes) — diff from empty, so container labels + the focused leaf are read,
        /// e.g. "Main Menu, Continue".
        /// </summary>
        public void AnnounceCurrent() => AnnounceDelta(EmptyPath);

        /// <summary>Move focus to a specific element (e.g. a node just inserted into the tree) and announce
        /// the change. Queues (doesn't interrupt) so a preceding feedback line still plays.</summary>
        public void Focus(UIElement target)
        {
            if (target == null) return;
            var snapshot = new List<UIElement>(Path);
            BuildPathTo(target);
            AnnounceDelta(snapshot);
        }

        /// <summary>Append an element to the path; if it's a container, descend to its representative leaf.
        /// Descent STOPS at a tree node (focus lands on the top-level node; its children are reached by
        /// expanding/arrowing, not by descending into them).</summary>
        protected void AppendWithDescend(UIElement element)
        {
            while (element != null)
            {
                Path.Add(element);
                var container = element as Container;
                if (container == null) return;
                var next = RepresentativeChild(container);
                container.SetFocusedChild(next);
                if (container.Shape == ContainerShape.Tree)
                {
                    if (next != null) Path.Add(next); // land ON the top-level node; don't descend into it
                    return;
                }
                element = next;
            }
        }

        /// <summary>The child to land on when first focusing a container: the remembered focus, else — for a
        /// single-select <b>list or tree</b> (radio buttons, tabs, the deity tree) — the currently-selected
        /// DIRECT child, else the first focusable. Only the top level is considered: descent stops at a tree
        /// node, and tree stepping/expanding never prefers selected, so expanding a node won't yank focus to
        /// a selected descendant. Panels/grids don't prefer selected.</summary>
        protected static UIElement RepresentativeChild(Container c)
        {
            if (c == null) return null;
            if (c.FocusedChild != null && c.FocusedChild.CanFocus) return c.FocusedChild;
            if (c.Shape == ContainerShape.VerticalList || c.Shape == ContainerShape.HorizontalList
                || c.Shape == ContainerShape.Tree)
            {
                var selected = SelectedChild(c);
                if (selected != null) return selected;
            }
            return c.FirstFocusable();
        }

        private static UIElement SelectedChild(Container c)
        {
            foreach (var child in c.Children)
                if (child.CanFocus && ReportsSelected(child)) return child;
            return null;
        }

        // An element is "selected" if it yields a SelectedAnnouncement that renders non-empty (single-select
        // controls render "selected" only when selected). Checkboxes/toggles use ValueAnnouncement, not
        // SelectedAnnouncement, so they never count here.
        private static bool ReportsSelected(UIElement e)
        {
            var ctx = new AnnouncementContext(e);
            foreach (var a in e.GetFocusAnnouncements())
                if (a is SelectedAnnouncement)
                {
                    var m = a.Render(ctx);
                    if (m != null && !m.IsEmpty) return true;
                }
            return false;
        }

        /// <summary>
        /// Diff a pre-move snapshot against the settled path and speak the delta:
        /// newly-entered nodes in path order (descend/sibling), or just the new
        /// innermost element (ascend). Called once, after the move is complete.
        /// </summary>
        protected void AnnounceDelta(List<UIElement> oldPath, bool interrupt = false)
        {
            // interrupt == true marks an actual focus MOVE (arrow/tab), not a screen-entry readout —
            // so it's where the game would play its control-hover sound.
            if (interrupt) WrathAccess.UiSound.Hover();

            int i = 0;
            while (i < oldPath.Count && i < Path.Count && oldPath[i] == Path[i]) i++;

            if (i < Path.Count)
            {
                var sb = new List<string>();
                for (int j = i; j < Path.Count; j++)
                {
                    // Skip a container whose label just duplicates the node beneath it (e.g. a
                    // "Game difficulty" section wrapping the "Game difficulty" control).
                    if (j + 1 < Path.Count)
                    {
                        var label = Path[j].GetLabelText();
                        if (!string.IsNullOrEmpty(label) && label == Path[j + 1].GetLabelText())
                            continue;
                    }
                    var d = Path[j].GetFocusMessage().Resolve();
                    if (!string.IsNullOrEmpty(d)) sb.Add(d);
                }
                if (sb.Count > 0) Speak(string.Join(", ", sb), interrupt);
            }
            else if (Current != null)
            {
                Speak(Current.GetFocusMessage().Resolve(), interrupt); // ascended: announce the now-innermost focus
            }
        }

        /// <summary>Rebuild the focus path as the ancestor chain from the screen down to <paramref name="target"/>.</summary>
        protected void BuildPathTo(UIElement target)
        {
            Path.Clear();
            if (target == null) return;
            var chain = new List<UIElement>();
            var e = target;
            while (e != null && e != Screen)
            {
                chain.Add(e);
                if (e.Parent != null) e.Parent.SetFocusedChild(e);
                e = e.Parent;
            }
            chain.Reverse();
            Path.AddRange(chain);
        }

        /// <summary>
        /// Ordered Tab-stops for the screen: descend through Panels; a List/Grid is a
        /// single stop (its current/first item); leaves under a Panel are stops.
        /// </summary>
        protected List<UIElement> ComputeTabStops()
        {
            var stops = new List<UIElement>();
            if (Screen != null) AddStops(Screen, stops);
            return stops;
        }

        private static void AddStops(Container c, List<UIElement> stops)
        {
            if (c.Shape != ContainerShape.Panel)
            {
                var item = RepresentativeChild(c);
                if (item != null) stops.Add(item);
                return;
            }
            var children = c.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is Container cc) AddStops(cc, stops);
                else if (child.CanFocus) stops.Add(child);
            }
        }

        // interrupt: true for focus MOVES (so held key-repeat reads the item you land on
        // instead of backing up a queue); false for screen-entry "where am I" readouts.
        protected static void Speak(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt);
        }
    }
}
