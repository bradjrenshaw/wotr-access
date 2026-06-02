using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One step in the chargen roadmap (the game's top strip). Announces the phase name, its state
    /// (current / completed / locked), and a live per-phase summary (the value the strip shows — class
    /// name, the six ability scores, etc.; see <see cref="WrathAccess.Screens.RoadmapSummary"/>).
    /// Activating jumps to that phase: the phases radio group's selected entity is CurrentPhaseVM, so
    /// SetSelectedFromView navigates there — gated on availability, exactly like the game's roadmap.
    /// </summary>
    public sealed class ProxyRoadmapEntry : UIElement
    {
        // A roadmap step reads as a tab — share the "tab" settings category + announcement order.
        public override System.Type AnnouncementOrderType => typeof(ProxyTab);

        private readonly CharGenPhaseBaseVM _vm;
        private readonly Func<string> _summary;

        public ProxyRoadmapEntry(CharGenPhaseBaseVM vm, Func<string> summary)
        {
            _vm = vm;
            _summary = summary;
        }

        private bool Available => _vm != null && _vm.IsAvailable.Value;
        private bool IsCurrent => _vm != null && _vm.IsSelected.Value;
        private bool IsCompleted => _vm != null && _vm.IsCompletedAndAvailible.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.PhaseName.Value ?? ""));
            yield return new RoleAnnouncement("tab");

            // State (current/completed) + the live summary, as one value; "locked" reads via Enabled.
            string state = IsCurrent ? "current" : (IsCompleted ? "completed" : null);
            string sum = _summary?.Invoke();
            string val = state;
            if (!string.IsNullOrEmpty(sum)) val = string.IsNullOrEmpty(val) ? sum : val + ", " + sum;
            if (!string.IsNullOrEmpty(val)) yield return new ValueAnnouncement(Message.Raw(val));

            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Can't "go to" the phase you're already on; locked phases can't be jumped to.
            if (Available && !IsCurrent)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Go to step"),
                    _ => _vm.SetSelectedFromView(true));
        }
    }
}
