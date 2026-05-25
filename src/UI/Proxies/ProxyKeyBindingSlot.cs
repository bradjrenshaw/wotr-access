using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using Kingmaker.UI.MVVM._VM.Settings.KeyBindSetupDialog; // GetPrettyString
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One binding slot of a key-binding row (index 0 = primary, 1 = secondary). Value is the
    /// bound key. Primary action rebinds it (opens the capture dialog); secondary clears it.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement))]
    public sealed class ProxyKeyBindingSlot : UIElement
    {
        private readonly SettingEntityKeyBindingVM _vm;
        private readonly int _index;
        private readonly string _label;

        public ProxyKeyBindingSlot(SettingEntityKeyBindingVM vm, int index, string label)
        {
            _vm = vm;
            _index = index;
            _label = label;
        }

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        // Mirrors the row view: GetPrettyString is empty for an unbound slot.
        private string KeyText()
        {
            if (_vm == null) return "not bound";
            var data = _index == 0 ? _vm.TempBindingValue1.Value : _vm.TempBindingValue2.Value;
            string p = data.GetPrettyString();
            return string.IsNullOrEmpty(p) ? "not bound" : p;
        }

        public override bool ReannounceOnContext => true; // announce the new value after clearing

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("button");
            yield return new ValueAnnouncement(Message.Raw(KeyText()));
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Rebind"), _ => OpenCapture());
            yield return new ElementAction(ActionIds.Context, Message.Raw("Clear"), _ => Clear());
        }

        private void OpenCapture()
        {
            KeyBindCaptureScreen.PendingLabel = (_vm?.Title ?? "") + ", " + _label;
            _vm.OpenBindingDialogVM(_index);
        }

        // Open the dialog and immediately unbind via its VM — clears the slot without ever
        // surfacing the capture screen (the dialog is created and closed within this frame).
        private void Clear()
        {
            _vm.OpenBindingDialogVM(_index);
            KeyBindCaptureScreen.Dialog()?.Unbind();
        }
    }
}
