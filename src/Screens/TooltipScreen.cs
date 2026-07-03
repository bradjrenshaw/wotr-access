using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// On-demand reader for a "complex" (brick) tooltip — opened with Space on a focused element that
    /// provides a TooltipBaseTemplate and/or inline glossary links. Each tooltip page is a CHILD SCREEN:
    /// <see cref="Open"/>/<see cref="OpenMenu"/> push one onto the current screen — the element's screen on
    /// a fresh open, the current page when drilling deeper (Space from inside the reader). So following a
    /// link or nested tooltip just pushes another child and Back pops one: the child-screen stack IS the
    /// drill stack, and focus returns to where you were automatically (per-screen graph state). A DOC page
    /// renders a template's rows as graph nodes (<see cref="TooltipFlowBuilder.Emit"/>); a MENU page lists
    /// the element's own tooltip + each inline link when there's more than one drill target.
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private readonly TooltipBaseTemplate _doc;                  // doc page (null ⇒ menu page)
        private readonly string _title;                             // menu page
        private readonly List<string> _labels;
        private readonly List<Func<TooltipBaseTemplate>> _opens;

        private TooltipScreen(TooltipBaseTemplate doc) { _doc = doc; }
        private TooltipScreen(string title, List<string> labels, List<Func<TooltipBaseTemplate>> opens)
        { _title = title; _labels = labels; _opens = opens; }

        /// <summary>Open a tooltip document page (pushed as a child of the current screen / page).</summary>
        public static void Open(TooltipBaseTemplate template)
        {
            if (template != null) ScreenManager.Current?.PushChild(new TooltipScreen(template));
        }

        /// <summary>Open a chooser page: the element's own tooltip plus its inline links (parallel
        /// label/factory lists). Used when a focused element offers more than one drill target.</summary>
        public static void OpenMenu(string title, List<string> labels, List<Func<TooltipBaseTemplate>> opens)
        {
            if (labels != null && labels.Count > 0)
                ScreenManager.Current?.PushChild(new TooltipScreen(title, labels, opens));
        }

        /// <summary>Follow an element's drill-in from Space: its template alone opens directly; inline
        /// glossary links (alone or alongside the template) go through the chooser page; nothing at all
        /// speaks the "no tooltip" line. The graph rows' shared OnTooltip handler.</summary>
        public static void FollowElement(UIElement el)
        {
            if (el == null) return;
            var tpl = el.GetTooltipTemplate();
            var links = TooltipLinks.Extract(el.GetLinkSourceText(), el.ResolveLink);

            int targets = (tpl != null ? 1 : 0) + links.Count;
            if (targets == 0) { Tts.Speak(Loc.T("nav.no_tooltip")); return; }
            if (tpl != null && links.Count == 0) { Open(tpl); return; }

            var labels = new List<string>();
            var opens = new List<Func<TooltipBaseTemplate>>();
            if (tpl != null) { var own = tpl; labels.Add(Loc.T("tooltip.view")); opens.Add(() => own); }
            foreach (var lk in links) { labels.Add(lk.Label); opens.Add(lk.Open); }
            OpenMenu(Loc.T("tooltip.links_title"), labels, opens);
        }

        public override string Key => "overlay.tooltip";
        // No ScreenName: opening jumps straight to the content (you pressed the key to read it fast).
        public override bool IsActive() => false; // only ever a child — the drill stack lives in the child tree

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back drills out one level (pop this page) — or closes the reader at the root page.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            string k = "tt:" + GetHashCode() + ":"; // per page instance (each drill level is its own screen)
            if (_doc != null)
            {
                TooltipFlowBuilder.Emit(b, k, _doc);
                return;
            }

            b.PushContext(_title);
            for (int i = 0; i < _labels.Count; i++)
            {
                var open = _opens[i]; // capture
                var label = _labels[i];
                b.AddItem(ControlId.Structural(k + "link:" + i),
                    GraphNodes.Button(() => label, () => { var t = open(); if (t != null) Open(t); }, sound: null));
            }
            b.PopContext();
        }
    }
}
