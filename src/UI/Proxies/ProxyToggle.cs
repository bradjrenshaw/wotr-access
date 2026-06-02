using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A boolean setting → toggle (the game's term for a checkbox). Value is "on"/"off" (read live);
    /// activate flips it. Announced "Label, toggle, on, [disabled]".
    /// </summary>
    public sealed class ProxyToggle : UIElement
    {
        // Shares the "toggle" settings category + announcement order (see ProxyBoolToggle).
        public override System.Type AnnouncementOrderType => typeof(ProxyBoolToggle);

        private readonly SettingsEntityBoolVM _vm;

        public ProxyToggle(SettingsEntityBoolVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // toggling flips the value in place
        public override Kingmaker.UI.UISoundType? ActivateSound => Kingmaker.UI.UISoundType.SettingsSwitchToggle;

        // ModificationAllowed is a snapshot taken at VM construction. Fine at the main
        // menu (nothing locks settings there); for in-game we'd read the live source.
        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(_vm != null && _vm.GetTempValue()
                ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.Raw(_vm?.Description));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"), _ => _vm.ChangeValue());
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => WrathAccess.UI.Tooltips.SimpleTooltip.Make(_vm?.Title, _vm?.Description);
    }
}
