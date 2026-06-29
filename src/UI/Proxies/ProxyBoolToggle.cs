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
        private readonly Func<bool, string> _announceChange; // self-announce mode (poll + speak on change) if set
        private readonly Func<int> _announceContext;         // optional: rebaseline silently when this changes

        public ProxyBoolToggle(string label, Func<bool> isChecked, Action onToggle,
            Func<bool> isEnabled = null, bool hideWhenDisabled = false,
            Func<bool, string> announceChange = null, Func<int> announceContext = null)
        {
            _label = label;
            _isChecked = isChecked;
            _onToggle = onToggle;
            _isEnabled = isEnabled;
            _hideWhenDisabled = hideWhenDisabled;
            _announceChange = announceChange;
            _announceContext = announceContext;
        }

        private bool Enabled => _isEnabled == null || _isEnabled();

        // When the underlying option isn't applicable (and hideWhenDisabled), drop out of nav
        // entirely — matching a game control that's hidden rather than greyed out. Live, so it
        // appears/disappears as the dependency changes without a rebuild.
        public override bool CanFocus => !_hideWhenDisabled || Enabled;

        // Self-announce mode is the single source of truth for the value readout, so don't ALSO re-read on
        // activate (which would catch the optimistic pre-settle value). Plain toggles re-announce as before.
        public override bool ReannounceOnActivate => _announceChange == null;

        // Self-announce: poll the bound state and speak when it CHANGES — so the element naturally reflects the
        // game state it's tied to (e.g. Hold), no matter how the change was caused (our hotkey, the HUD button,
        // or the game) and regardless of whether anything is focused. The screen ticks this every frame; the
        // navigator also ticks it while focused (the second tick is a no-op — the value already matches).
        // announceContext guards the all-or-nothing aggregates (Hold/Stop = "all selected …", which flip on
        // every character swap): when it changes, rebaseline silently rather than announce a non-toggle flip.
        private bool _selfArmed;
        private bool _selfLast;
        private int _selfCtx;

        public override void OnUpdate()
        {
            if (_announceChange == null || _isChecked == null) return;
            bool now = _isChecked();
            int ctx = _announceContext != null ? _announceContext() : 0;
            if (!_selfArmed) { _selfArmed = true; _selfLast = now; _selfCtx = ctx; return; } // baseline, silent
            if (ctx != _selfCtx) { _selfCtx = ctx; _selfLast = now; return; }                // context changed → rebaseline
            if (now == _selfLast) return;
            _selfLast = now;
            var line = _announceChange(now);
            if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: false);
        }
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
