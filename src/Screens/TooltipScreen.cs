using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// On-demand reader for a "complex" (brick) tooltip — opened with Space on a focused element that
    /// provides a TooltipBaseTemplate and/or inline glossary links. Everything lives in ONE screen as a
    /// stack of PAGES (no separate chooser screen — that raced the rebuild and lost focus):
    ///  • a <b>document</b> page renders a template as a FlowSheet (<see cref="TooltipFlowBuilder"/>);
    ///  • a <b>menu</b> page lists the element's own tooltip + each inline link, when there's more than one.
    /// Following a link or a nested tooltip PUSHES a page; Back pops. Each page caches its built sheet and
    /// remembers its focused row, so backing out lands you exactly where you were. Layer 40 (top) so it
    /// reads over any screen.
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        // A page in the drill stack: its sheet is built once and CACHED (so the cells survive a drill-out),
        // and the last focused cell is remembered for restore-on-return.
        private abstract class Page
        {
            public FlowSheet Sheet;
            public UIElement SavedFocus;
            public abstract FlowSheet Build();
        }

        private sealed class DocPage : Page
        {
            private readonly TooltipBaseTemplate _template;
            public DocPage(TooltipBaseTemplate t) { _template = t; }
            public override FlowSheet Build() => TooltipFlowBuilder.Build(_template);
        }

        private sealed class MenuPage : Page
        {
            private readonly string _title;
            private readonly List<string> _labels;
            private readonly List<Func<TooltipBaseTemplate>> _opens;
            public MenuPage(string title, List<string> labels, List<Func<TooltipBaseTemplate>> opens)
            { _title = title; _labels = labels; _opens = opens; }

            public override FlowSheet Build()
            {
                var sheet = new FlowSheet(_title);
                var list = sheet.List(null);
                for (int i = 0; i < _labels.Count; i++)
                {
                    var open = _opens[i]; // capture
                    list.Item(new ProxyActionButton(_labels[i], null,
                        () => { var t = open(); if (t != null) PushDoc(t); }, actionVerb: "choose"));
                }
                sheet.Reflow();
                return sheet;
            }
        }

        private static readonly List<Page> s_stack = new List<Page>();

        /// <summary>Open a tooltip. While the reader is closed this starts fresh; while it's open (any
        /// Open from a row inside it) it's a drill-in push onto the same stack.</summary>
        public static void Open(TooltipBaseTemplate template)
        {
            if (template != null) s_stack.Add(new DocPage(template));
        }

        /// <summary>Open a chooser page: the element's own tooltip plus its inline links (parallel
        /// label/factory lists). Used when a focused element offers more than one drill target.</summary>
        public static void OpenMenu(string title, List<string> labels, List<Func<TooltipBaseTemplate>> opens)
        {
            if (labels != null && labels.Count > 0) s_stack.Add(new MenuPage(title, labels, opens));
        }

        private static void PushDoc(TooltipBaseTemplate t) { if (t != null) s_stack.Add(new DocPage(t)); }

        /// <summary>Dismiss the whole stack (external close).</summary>
        public static void CloseTooltip() => s_stack.Clear();

        public override string Key => "overlay.tooltip";
        // No ScreenName: opening jumps straight to the content (you pressed the key to read it fast).
        public override int Layer => 40; // above everything — a tooltip can be read over any screen
        public override bool IsActive() => s_stack.Count > 0;

        private static Page Top => s_stack.Count > 0 ? s_stack[s_stack.Count - 1] : null;
        private Page _built;

        public override void OnPush() { _built = null; Render(announce: false); }
        public override void OnPop() { Clear(); _built = null; s_stack.Clear(); }

        // Push/pop within the stack doesn't change the active screen, so ScreenManager won't announce —
        // rebuild and re-seat focus here (remembering where we were on the page we're leaving).
        public override void OnUpdate()
        {
            var top = Top;
            if (top == _built) return;
            if (_built != null && Navigation.Current != null) _built.SavedFocus = Navigation.Current;
            Render(announce: true);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back pops one level: drill-out to the parent page, or close the reader at the root.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => { if (s_stack.Count > 0) s_stack.RemoveAt(s_stack.Count - 1); });
        }

        private void Render(bool announce)
        {
            Clear();
            _built = Top;
            if (_built == null) return;
            if (_built.Sheet == null) _built.Sheet = _built.Build();
            Add(_built.Sheet);
            // The initial open IS a screen change, so OnPush passes announce:false and lets ScreenManager
            // announce; a drill push/pop isn't a screen change, so we announce (and restore focus) here.
            if (announce)
                Navigation.Focus(_built.SavedFocus ?? _built.Sheet.FirstFocusable());
        }
    }
}
