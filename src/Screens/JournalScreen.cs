using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Journal; // JournalVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The journal service window (<see cref="JournalVM"/>): the grouped quest list on top (each quest reads
    /// its state; Enter selects it) and the selected quest's detail below — title, description, objectives
    /// (with their state) and addendums, plus completion text for finished quests. Content refills when the
    /// selection or any quest/objective state changes, restoring the cursor by grid position. Escape closes.
    /// </summary>
    public sealed class JournalScreen : Screen
    {
        public override string Key => "service.Journal";
        public override string ScreenName => "Journal";
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Journal;

        private Container _content;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel;

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; }

        public override void OnUpdate()
        {
            var jv = Jv();
            if (jv == null) return;
            if (!_built) BuildShell();
            var sig = Sig(jv);
            if (sig != _sig) { _sig = sig; RefillContent(jv); }
            else _lastRestoreLabel = null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static JournalVM Jv()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.JournalVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        // Refreshes on selection change and on any quest / objective state change.
        private static string Sig(JournalVM jv)
        {
            var sb = new StringBuilder();
            sb.Append(jv.SelectedQuest?.Value?.Blueprint?.name).Append('|');
            var groups = jv.Navigation?.NavigationGroups;
            if (groups != null)
                foreach (var g in groups)
                {
                    if (g?.Quests == null) continue;
                    foreach (var q in g.Quests)
                        if (q != null) sb.Append(q.Title).Append(q.IsCompleted ? 'C' : q.IsFailed ? 'F' : 'A').Append(q.IsAttention ? '!' : '.').Append(',');
                }
            sb.Append('|');
            var det = jv.Quest?.Value;
            if (det?.Objectives != null)
                foreach (var o in det.Objectives) if (o != null) sb.Append(o.IsCompleted ? 'C' : o.IsFailed ? 'F' : 'A');
            return sb.ToString();
        }

        private void BuildShell()
        {
            _built = true;
            Clear();
            _content = new Panel();
            Add(_content);
            Navigation.Attach(this);
        }

        private void RefillContent(JournalVM jv)
        {
            if (_content == null) return;
            var cap = CaptureFocus();
            _content.Clear();
            BuildQuestList(jv);
            BuildDetail(jv);
            RestoreFocus(cap);
        }

        // The grouped quest list: a region per quest group, each quest a selectable entry.
        private void BuildQuestList(JournalVM jv)
        {
            var groups = jv.Navigation?.NavigationGroups;
            var sheet = new FlowSheet("Quests");
            bool any = false;
            if (groups != null)
                foreach (var g in groups)
                {
                    if (g?.Quests == null || g.Quests.Count == 0) continue;
                    var r = sheet.List(g.Title);
                    foreach (var q in g.Quests) if (q != null) { r.Item(new ProxyJournalQuest(q)); any = true; }
                }
            if (!any) sheet.List(null).Item(new TextElement("No quests."));
            sheet.Reflow();
            _content.Add(sheet);
        }

        // The selected quest's detail: title + description (+ completion text), then its objectives and their
        // addendums, each with its state.
        private void BuildDetail(JournalVM jv)
        {
            var q = jv.Quest?.Value;
            if (q == null) { _content.Add(new TextElement("Select a quest.")); return; }

            var sheet = new FlowSheet("Quest");
            var head = sheet.List(null);
            head.Item(new TextElement(q.Title, "heading"));
            if (!string.IsNullOrWhiteSpace(q.Description)) head.Item(new TextElement(q.Description));
            if (q.IsCompleted && !string.IsNullOrWhiteSpace(q.CompletionText)) head.Item(new TextElement(q.CompletionText));

            if (q.Objectives != null && q.Objectives.Count > 0)
            {
                var obj = sheet.List("Objectives");
                foreach (var o in q.Objectives)
                {
                    if (o == null) continue;
                    var text = string.IsNullOrWhiteSpace(o.Description) ? o.Title : o.Description;
                    obj.Item(new TextElement(text + " (" + StateWord(o.IsCompleted, o.IsFailed) + ")"));
                    if (o.Addendums != null)
                        foreach (var a in o.Addendums)
                            if (a != null) obj.Item(new TextElement("  " + a.Description + " (" + StateWord(a.IsCompleted, a.IsFailed) + ")"));
                }
            }

            sheet.Reflow();
            _content.Add(sheet);
        }

        private static string StateWord(bool completed, bool failed)
            => Message.Localized("ui", completed ? "journal.completed" : failed ? "journal.failed" : "journal.active").Resolve();

        // (contentChildIndex, row, col) of the focused cell, or child = -1 when focus is outside the content.
        private (int child, int row, int col) CaptureFocus()
        {
            var cur = Navigation.Active?.Current;
            if (cur != null)
                for (int i = 0; i < _content.Children.Count; i++)
                    if (_content.Children[i] is FlowSheet fs && fs.TryCoords(cur, out int r, out int c))
                        return (i, r, c);
            return (-1, 0, 0);
        }

        private void RestoreFocus((int child, int row, int col) cap)
        {
            if (cap.child < 0) return;
            UIElement cell = null;
            if (cap.child < _content.Children.Count && _content.Children[cap.child] is FlowSheet fs && fs.RowCount > 0)
            {
                int r = System.Math.Min(cap.row, fs.RowCount - 1);
                int c = fs.Visitable(r, cap.col) ? cap.col : fs.LeftmostVisitable(r);
                if (c >= 0) cell = fs.CellAt(r, c);
            }
            cell = cell ?? _content.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }
    }
}
