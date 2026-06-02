using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>One option row in a choice submenu (e.g. a dropdown's options). Activate selects it.</summary>
    public sealed class ProxyChoiceOption : UIElement
    {
        // Shares the "radio button" settings category + announcement order (see ProxySelectionItem).
        public override System.Type AnnouncementOrderType => typeof(ProxySelectionItem);

        private readonly string _label;
        private readonly bool _selected;
        private readonly Action _select;

        public ProxyChoiceOption(string label, bool selected, Action select)
        {
            _label = label;
            _selected = selected;
            _select = select;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_selected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"), _ => _select?.Invoke());
        }
    }
}
