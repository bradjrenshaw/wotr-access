using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A radio-button list item driven by delegates: a live label, an "is this the current selection?"
    /// predicate, and an activate action. The delegate counterpart of the VM-bound
    /// <see cref="ProxySelectionItem"/>, for radio lists over the mod's OWN state (e.g. the setup wizard's
    /// speech-engine picker). Announces label + "radio button" + selected, and re-announces in place on
    /// activate (selecting flips it). Activation side effects (e.g. playing a sample) live in the action.
    /// </summary>
    public sealed class ProxyRadioOption : UIElement
    {
        // Share ProxySelectionItem's announcement order + settings ("radio_button").
        public override Type AnnouncementOrderType => typeof(ProxySelectionItem);

        private readonly Func<string> _label;
        private readonly Func<bool> _selected;
        private readonly Action _activate;

        public ProxyRadioOption(Func<string> label, Func<bool> selected, Action activate)
        {
            _label = label;
            _selected = selected;
            _activate = activate;
        }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label != null ? _label() : ""));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_selected != null && _selected());
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ => _activate?.Invoke());
        }
    }
}
