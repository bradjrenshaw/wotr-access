using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Encyclopedia; // IPage, INode
using Kingmaker.PubSubSystem; // EventBus, IEncyclopediaHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Encyclopedia; // EncyclopediaVM, EncyclopediaNavigationElementVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Encyclopedia.Blocks; // block VMs
using WrathAccess.UI;
using WrathAccess.UI.Announcements;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The encyclopedia service window (<see cref="EncyclopediaVM"/>). The game has no search; its navigation
    /// is a fully-expandable hierarchy tree (chapters → pages → subpages), so we mirror that: a treeview of
    /// the whole hierarchy (built once, kept stable so expansion/position survive) plus the current page —
    /// title, text, and links to its child pages. Tree nodes lazily build their children on expand; Enter on
    /// any node loads its page and moves focus to the page so you read it. Navigation goes through the
    /// IEncyclopediaHandler EventBus (like the game's own clicks) so the on-screen visuals stay in sync. We
    /// keep our own history so Escape goes back, then closes the window at the root. Class-progression /
    /// bestiary / image blocks are noted but not yet rendered.
    /// </summary>
    public sealed class EncyclopediaScreen : Screen
    {
        public override string Key => "service.Encyclopedia";
        public override string ScreenName => Loc.T("screen.encyclopedia");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Encyclopedia;

        private TreeGroup _nav;     // the hierarchy tree (built once, stable)
        private Container _page;    // the current page (rebuilt on navigation)
        private bool _built;
        private bool _navigated;
        private string _sig;
        private string _lastPageLabel;
        private readonly Stack<IPage> _history = new Stack<IPage>();

        public override void OnPush() { _built = false; _navigated = false; _sig = null; _lastPageLabel = null; _history.Clear(); }
        public override void OnPop() { Clear(); _nav = null; _page = null; _built = false; _history.Clear(); }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell(vm);
            var sig = Sig(vm);
            if (sig != _sig) { _sig = sig; RefillPage(vm); }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // History-back is a page-view notion: only when focus is in the page and we've drilled via a link.
            // From the tree (a jump-anywhere navigator), or with nothing to go back to, Escape closes.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                if (!FocusInTree() && _history.Count > 0) Back();
                else ServiceWindows()?.HandleCloseAll();
            });
        }

        private bool FocusInTree()
        {
            for (var e = Navigation.Active?.Current; e != null; e = e.Parent)
                if (ReferenceEquals(e, _nav)) return true;
            return false;
        }

        private static EncyclopediaVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.EncyclopediaVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        private static string Sig(EncyclopediaVM vm) => vm.Page?.Value?.Page?.GetNavigationTitle() ?? "";

        // Jumping via the tree resets history — it's a "go anywhere" navigator, not a drill-down, so there's
        // nothing to go "back" to afterward. Drilling via an in-page link pushes history so Escape can return.
        private void NavigateJump(IPage node) { _history.Clear(); Go(node); }
        private void NavigateDrill(IPage node)
        {
            var cur = Vm()?.Page?.Value?.Page;
            if (cur != null) _history.Push(cur);
            Go(node);
        }

        // Route through the EventBus like the game's SelectPage, not vm.HandleEncyclopediaPage directly — the
        // VM, the navigation tree (highlight/expand) and breadcrumbs all subscribe as IEncyclopediaHandler, so
        // this keeps the on-screen visuals in sync, not just our reading.
        private void Go(IPage node)
        {
            if (Vm() == null || node == null) return;
            _navigated = true;
            EventBus.RaiseEvent<IEncyclopediaHandler>(x => x.HandleEncyclopediaPage(node, scrollToCenter: false));
        }

        private void Back()
        {
            if (_history.Count == 0) return;
            _navigated = true;
            var prev = _history.Pop();
            EventBus.RaiseEvent<IEncyclopediaHandler>(x => x.HandleEncyclopediaPage(prev, scrollToCenter: false));
        }

        private void BuildShell(EncyclopediaVM vm)
        {
            _built = true;
            Clear();

            // The full hierarchy tree (top-level chapters; each node lazily expands to its children).
            _nav = new TreeGroup();
            var chapters = vm.NavigationVM?.GetChapters();
            if (chapters != null)
                foreach (var ch in chapters)
                    if (ch != null) _nav.Add(new EncyclopediaNavNode(ch, NavigateJump));
            Add(_nav);

            _page = new Panel();
            Add(_page);

            Navigation.Attach(this);
        }

        // The current page: title, text, then links to its child pages. Rebuilt on navigation; the tree above
        // is left untouched so its expansion/position survive.
        private void RefillPage(EncyclopediaVM vm)
        {
            if (_page == null) return;
            _page.Clear();

            var page = vm.Page?.Value;
            var sheet = new FlowSheet(Loc.T("ency.page"));
            if (page == null)
            {
                sheet.List(null).Item(new TextElement(() => Loc.T("ency.select_topic")));
                sheet.Reflow();
                _page.Add(sheet);
                return;
            }

            var body = sheet.List(null);
            body.Item(new TextElement(page.Title, "heading"));
            foreach (var block in page.BlockVMs)
            {
                switch (block)
                {
                    case EncyclopediaPageBlockTextVM t when !string.IsNullOrWhiteSpace(t.Text):
                        body.Item(new TextElement(t.Text));
                        break;
                    case EncyclopediaPageBlockClassProgressionVM _:
                        body.Item(new TextElement(() => Loc.T("ency.progression_not_shown")));
                        break;
                    case EncyclopediaPageBlockUnitVM _:
                        body.Item(new TextElement(() => Loc.T("ency.stats_not_shown")));
                        break;
                    // Child pages are listed below; images are skipped.
                }
            }

            var childs = page.Page?.GetChilds();
            if (childs != null && childs.Count > 0)
            {
                var topics = sheet.List(Message.Localized("ui", "encyclopedia.topics").Resolve());
                foreach (var child in childs)
                {
                    if (child == null) continue;
                    var c = child;
                    topics.Item(new ProxyActionButton(() => c.GetNavigationTitle(), () => true, () => NavigateDrill(c), actionVerb: "open"));
                }
            }

            sheet.Reflow();
            _page.Add(sheet);

            if (_navigated) { _navigated = false; FocusPage(sheet); }
        }

        // After navigating, drop focus onto the top of the page (its title), not back into the tree.
        private void FocusPage(FlowSheet sheet)
        {
            if (sheet.RowCount == 0) return;
            int c = sheet.LeftmostVisitable(0);
            var cell = c >= 0 ? sheet.CellAt(0, c) : null;
            if (cell == null) return;
            _lastPageLabel = cell.GetLabelText();
            Navigation.Focus(cell, announce: true);
        }
    }

    /// <summary>
    /// One node in the encyclopedia navigation tree (<see cref="EncyclopediaNavigationElementVM"/>): a chapter
    /// or page. A Tree container so the navigator's Right/Left expand/collapse and up/down DFS work; children
    /// are built lazily on first expand (the hierarchy is large). Reads its title + expanded/collapsed state;
    /// Enter loads its page.
    /// </summary>
    internal sealed class EncyclopediaNavNode : Container
    {
        private readonly EncyclopediaNavigationElementVM _vm;
        private readonly Action<IPage> _navigate;
        private bool _built;

        public EncyclopediaNavNode(EncyclopediaNavigationElementVM vm, Action<IPage> navigate)
            : base(ContainerShape.Tree, vm.Title)
        {
            _vm = vm;
            _navigate = navigate;
        }

        public override bool Expandable => _vm.IsCanCollapse; // has children (even before they're built)

        public override void Expand()
        {
            if (!_built)
            {
                _built = true;
                foreach (var child in _vm.GetOrCreateChildsVM())
                    if (child != null) Add(new EncyclopediaNavNode(child, _navigate));
            }
            base.Expand();
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm.Title));
            if (Expandable) yield return new RoleAnnouncement(Expanded ? "expanded" : "collapsed");
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ => _navigate(_vm.Page));
        }
    }
}
