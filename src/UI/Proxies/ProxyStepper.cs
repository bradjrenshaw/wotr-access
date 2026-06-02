using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A generic point-buy +/- button (raise or lower one step), driven by delegates. Reads its
    /// cost/state label; Activate performs the step and re-announces a caller-supplied summary (the new
    /// value + remaining pool). Used as a grid cell, where the grid shows the label and the summary
    /// surfaces via the activate re-announcement. Carries a "button" role. Shared by any point-buy
    /// stepper (skill ranks, and reusable at level-up); the caller supplies live reads so values are
    /// fresh the instant you step.
    /// </summary>
    public sealed class ProxyStepper : UIElement
    {
        // A stepper reads as a button — share the "button" settings category + announcement order.
        public override System.Type AnnouncementOrderType => typeof(ProxyActionButton);

        private readonly Func<string> _label;
        private readonly Func<bool> _enabled;
        private readonly Action _act;
        private readonly Func<string> _summary;
        private readonly string _actionVerb;

        // actionVerb is a "ui" table key under "action." (e.g. "step", "raise", "lower").
        public ProxyStepper(Func<string> label, Func<bool> enabled, Action act, Func<string> summary,
            string actionVerb = "step")
        {
            _label = label;
            _enabled = enabled;
            _act = act;
            _summary = summary;
            _actionVerb = actionVerb;
        }

        public override bool ReannounceOnActivate => true;
        public override string Role => "button";

        private bool Enabled => _enabled == null || _enabled();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label != null ? _label() : ""));
            if (_summary != null) yield return new ValueAnnouncement(Message.Raw(_summary()));
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action." + _actionVerb), _ => _act?.Invoke());
        }
    }
}
