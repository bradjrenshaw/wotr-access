using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A generic checkbox over an arbitrary boolean (unlike <see cref="ProxyToggle"/>, which is
    /// tied to SettingsEntityBoolVM). Reads its value live and activates via a supplied toggle
    /// action — e.g. the Story phase's "Last Azlanti" mode (StoryVM.SwitchLastAzlanti).
    /// </summary>
    // Canonical "toggle" (the game's term for a checkbox): ProxyToggle / ProxyOverrideToggle share this
    // settings category + announcement order (the union across all three — Tooltip comes from ProxyToggle).
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("toggle")]
    public sealed class ProxyBoolToggle : UIElement
    {
        private readonly string _label;
        private readonly Func<bool> _isChecked;
        private readonly Action _onToggle;
        private readonly Func<bool> _isEnabled;
        private readonly bool _hideWhenDisabled;

        public ProxyBoolToggle(string label, Func<bool> isChecked, Action onToggle,
            Func<bool> isEnabled = null, bool hideWhenDisabled = false)
        {
            _label = label;
            _isChecked = isChecked;
            _onToggle = onToggle;
            _isEnabled = isEnabled;
            _hideWhenDisabled = hideWhenDisabled;
        }

        private bool Enabled => _isEnabled == null || _isEnabled();

        // When the underlying option isn't applicable (and hideWhenDisabled), drop out of nav
        // entirely — matching a game control that's hidden rather than greyed out. Live, so it
        // appears/disappears as the dependency changes without a rebuild.
        public override bool CanFocus => !_hideWhenDisabled || Enabled;

        public override bool ReannounceOnActivate => true; // flips in place → re-announce "on"/"off"
        public override Kingmaker.UI.UISoundType? ActivateSound => Kingmaker.UI.UISoundType.SettingsSwitchToggle;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label ?? ""));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(_isChecked != null && _isChecked()
                ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"));
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"), _ => _onToggle?.Invoke());
        }
    }
}
