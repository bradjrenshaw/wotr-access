using System.Collections.Generic;
using WrathAccess.Input;
using WrathAccess.Screens;

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

        /// <summary>Append an element to the path; if it's a container, descend to its first leaf.</summary>
        protected void AppendWithDescend(UIElement element)
        {
            while (element != null)
            {
                Path.Add(element);
                var container = element as Container;
                if (container == null) return;
                container.SetFocusedChild(container.FirstFocusable());
                element = container.FirstFocusable();
            }
        }

        /// <summary>
        /// Diff a pre-move snapshot against the settled path and speak the delta:
        /// newly-entered nodes in path order (descend/sibling), or just the new
        /// innermost element (ascend). Called once, after the move is complete.
        /// </summary>
        protected void AnnounceDelta(List<UIElement> oldPath, bool interrupt = false)
        {
            int i = 0;
            while (i < oldPath.Count && i < Path.Count && oldPath[i] == Path[i]) i++;

            if (i < Path.Count)
            {
                var sb = new List<string>();
                for (int j = i; j < Path.Count; j++)
                {
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
                var item = (c.FocusedChild != null && c.FocusedChild.CanFocus) ? c.FocusedChild : c.FirstFocusable();
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
