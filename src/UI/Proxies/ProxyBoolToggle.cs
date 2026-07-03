using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate (optional glossary tooltip)
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A generic checkbox over an arbitrary boolean (unlike <see cref="ProxyToggle"/>, which is
    /// tied to SettingsEntityBoolVM). Reads its value live and activates via a supplied toggle
    /// action — e.g. the Story phase's "Last Azlanti" mode (StoryVM.SwitchLastAzlanti).
    /// </summary>
    // Canonical "toggle" (the game's term for a checkbox): ProxyToggle / the override-toggle node factory share this
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
        private readonly bool _reannounceOnActivate;
        private readonly Func<TooltipBaseTemplate> _tooltip; // optional tooltip (Space), resolved live

        public ProxyBoolToggle(string label, Func<bool> isChecked, Action onToggle,
            Func<bool> isEnabled = null, bool hideWhenDisabled = false,
            Func<TooltipBaseTemplate> tooltip = null, bool reannounceOnActivate = true)
        {
            _label = label;
            _isChecked = isChecked;
            _onToggle = onToggle;
            _isEnabled = isEnabled;
            _hideWhenDisabled = hideWhenDisabled;
            _tooltip = tooltip;
            _reannounceOnActivate = reannounceOnActivate;
        }

        public override TooltipBaseTemplate GetTooltipTemplate() => _tooltip != null ? _tooltip() : null;

        private bool Enabled => _isEnabled == null || _isEnabled();

        // When the underlying option isn't applicable (and hideWhenDisabled), drop out of nav
        // entirely — matching a game control that's hidden rather than greyed out. Live, so it
        // appears/disappears as the dependency changes without a rebuild.
        public override bool CanFocus => !_hideWhenDisabled || Enabled;

        // False for toggles whose activation settles ASYNCHRONOUSLY in the game (Hold/Stop/etc.): an
        // instant re-read would speak the optimistic pre-settle value; a state watcher (e.g.
        // PartyStateWatch) speaks the settled truth instead.
        public override bool ReannounceOnActivate => _reannounceOnActivate;

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
