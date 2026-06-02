using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A boolean setting → checkbox. Value is "checked"/"unchecked" (read live);
    /// activate flips it. Announced "Label, checkbox, checked, [disabled]".
    /// </summary>
    public sealed class ProxyToggle : UIElement
    {
        // Shares the "checkbox" settings category + announcement order (see ProxyBoolToggle).
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
            yield return new RoleAnnouncement("checkbox");
            yield return new ValueAnnouncement(Message.Raw(_vm != null && _vm.GetTempValue() ? "checked" : "unchecked"));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.Raw(_vm?.Description));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Toggle"), _ => _vm.ChangeValue());
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => WrathAccess.UI.Tooltips.SimpleTooltip.Make(_vm?.Title, _vm?.Description);
    }
}
