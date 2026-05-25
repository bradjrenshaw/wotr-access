using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A boolean setting → checkbox. Value is "checked"/"unchecked" (read live);
    /// activate flips it. Announced "Label, checkbox, checked, [disabled]".
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyToggle : UIElement
    {
        private readonly SettingsEntityBoolVM _vm;

        public ProxyToggle(SettingsEntityBoolVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // toggling flips the value in place

        // ModificationAllowed is a snapshot taken at VM construction. Fine at the main
        // menu (nothing locks settings there); for in-game we'd read the live source.
        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("checkbox");
            yield return new ValueAnnouncement(Message.Raw(_vm != null && _vm.GetTempValue() ? "checked" : "unchecked"));
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Toggle"), _ => _vm.ChangeValue());
        }
    }
}
