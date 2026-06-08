using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Journal; // JournalQuestVM
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One quest in the journal's quest list (<see cref="JournalQuestVM"/>). The quests form a selection
    /// group (the selected one is shown in the detail panel), so each is a <b>radio button</b>: it announces
    /// "selected" when it's the shown quest, plus the quest's state (active / completed / failed, and
    /// "updated" when it needs attention). Enter selects it (the game's SelectQuest), updating the detail.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement))]
    public sealed class ProxyJournalQuest : UIElement
    {
        private readonly JournalQuestVM _vm;

        public ProxyJournalQuest(JournalQuestVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private string State()
        {
            var key = _vm.IsCompleted ? "journal.completed" : _vm.IsFailed ? "journal.failed" : "journal.active";
            var s = Message.Localized("ui", key).Resolve();
            if (_vm.IsAttention) s += ", " + Message.Localized("ui", "journal.updated").Resolve();
            return s;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm.Title));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_vm.IsSelected.Value);
            yield return new ValueAnnouncement(Message.Raw(State()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"), _ => _vm.SelectQuest());
        }
    }
}
