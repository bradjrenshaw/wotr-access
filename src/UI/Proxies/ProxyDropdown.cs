using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A dropdown setting → combo box. Primary opens a submenu listing all options (the only way to
    /// change it). It deliberately does NOT advertise Left/Right adjust: in a treeview those mean
    /// collapse/ascend, and stealing them for inline cycling is unintuitive. Value = current option.
    /// </summary>
    // Canonical "combo box": ProxyChoiceDropdown / ProxyDifficulty share this settings category +
    // announcement order (this VM's order is the union across the three).
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("combo_box")]
    public sealed class ProxyDropdown : UIElement
    {
        private readonly SettingsEntityDropdownVM _vm;

        public ProxyDropdown(SettingsEntityDropdownVM vm) { _vm = vm; }

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        private string CurrentOption()
        {
            if (_vm == null) return "";
            var vals = _vm.LocalizedValues;
            int i = _vm.GetTempValue();
            return (vals != null && i >= 0 && i < vals.Count) ? vals[i] : "";
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("combo box");
            yield return new ValueAnnouncement(Message.Raw(CurrentOption()));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.Raw(_vm?.Description));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Open"), _ => OpenSubmenu());
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => WrathAccess.UI.Tooltips.SimpleTooltip.Make(_vm?.Title, _vm?.Description);

        private void OpenSubmenu()
        {
            var vals = _vm?.LocalizedValues;
            if (vals == null) return;
            ChoiceSubmenuScreen.Open(_vm.Title, vals, _vm.GetTempValue(), i => _vm.SetTempValue(i));
        }
    }
}
