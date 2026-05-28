using System;
using System.Collections.Generic;
using Kingmaker.UI; // UISoundType
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
            yield return new ElementAction(ActionIds.Decrease, Message.Raw("Decrease"), _ => Step(-1));
            yield return new ElementAction(ActionIds.Increase, Message.Raw("Increase"), _ => Step(1));
            yield return new ElementAction(ActionIds.SetValue, Message.Raw("Set value"),
                a => SetValue((float)ActionArgs.Get<double>(a, "value")));
        }

        // Mirror SettingsEntitySliderPCView.SetValueFromUI: play the move sound only when the value
        // actually changes (so stepping at min/max stays silent). For volume sliders this doubles as
        // the level cue — SoundSettingsController updates the Wwise bus on the VM change, so the click
        // sounds at the new volume, just like the game.
        private void Step(int dir)
        {
            float before = _vm.GetTempValue();
            _vm.SetNextValue(dir);
            PlayMoveIfChanged(before);
        }

        private void SetValue(float value)
        {
            float before = _vm.GetTempValue();
            _vm.SetTempValue(value);
            PlayMoveIfChanged(before);
        }

        private void PlayMoveIfChanged(float before)
        {
            if (Math.Abs(_vm.GetTempValue() - before) > float.Epsilon)
                UiSound.Play(UISoundType.SettingsSliderMove);
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => WrathAccess.UI.Tooltips.SimpleTooltip.Make(_vm?.Title, _vm?.Description);
    }
}
