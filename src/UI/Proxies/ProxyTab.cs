using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A reusable tab for mod-built screens (delegate-driven, unlike the game-bound
    /// <c>ProxySettingsTab</c>). Announces its label, the "tab" role, and "selected" only when it's the
    /// active one; Activate selects it. Selecting re-announces in place (so the new "selected" is spoken),
    /// which is the feedback — the screen swaps the detail content on the next tick. Mirrors how the game's
    /// settings tabs read. Pattern: a list of these + a separate content region the active tab fills.
    /// </summary>
    // Canonical "tab": ProxySettingsTab / ProxyRoadmapEntry share this settings category +
    // announcement order (this is the union across the three — Value/Enabled come from those two).
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(SelectedAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyTab : UIElement
    {
        private readonly string _label;
        private readonly Func<bool> _isSelected;
        private readonly Action _onSelect;

        public ProxyTab(string label, Func<bool> isSelected, Action onSelect)
        {
            _label = label;
            _isSelected = isSelected;
            _onSelect = onSelect;
        }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("tab");
            yield return new SelectedAnnouncement(_isSelected != null && _isSelected());
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"), _ => _onSelect?.Invoke());
        }
    }
}
