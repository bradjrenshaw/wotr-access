using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A dropdown setting → combo box. Left/Right cycle options inline (quick path);
    /// primary opens a submenu listing all options. Value is the current option text.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
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
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            yield return new ElementAction(ActionIds.Decrease, Message.Raw("Previous option"),
                _ => { if (_vm.IsPrevValue) _vm.SetTempValue(_vm.GetTempValue() - 1); });
            yield return new ElementAction(ActionIds.Increase, Message.Raw("Next option"),
                _ => { if (_vm.IsNextValue) _vm.SetTempValue(_vm.GetTempValue() + 1); });
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Open"), _ => OpenSubmenu());
        }

        private void OpenSubmenu()
        {
            var vals = _vm?.LocalizedValues;
            if (vals == null) return;
            ChoiceSubmenuScreen.Open(_vm.Title, vals, _vm.GetTempValue(), i => _vm.SetTempValue(i));
        }
    }
}
