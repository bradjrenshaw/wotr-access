using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A numeric setting → slider. Left/Right step by the game's own SetNextValue;
    /// setValue sets directly. Value read live and formatted by IsInt/DecimalPlaces.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySlider : UIElement
    {
        private readonly SettingsEntitySliderVM _vm;

        public ProxySlider(SettingsEntitySliderVM vm) { _vm = vm; }

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        private string ValueText()
        {
            if (_vm == null) return "";
            float v = _vm.GetTempValue();
            return _vm.IsInt ? ((int)Math.Round(v)).ToString() : v.ToString("F" + _vm.DecimalPlaces);
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Raw(ValueText()));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.Raw(_vm?.Description));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            yield return new ElementAction(ActionIds.Decrease, Message.Raw("Decrease"), _ => _vm.SetNextValue(-1));
            yield return new ElementAction(ActionIds.Increase, Message.Raw("Increase"), _ => _vm.SetNextValue(1));
            yield return new ElementAction(ActionIds.SetValue, Message.Raw("Set value"),
                a => _vm.SetTempValue((float)ActionArgs.Get<double>(a, "value")));
        }
    }
}
