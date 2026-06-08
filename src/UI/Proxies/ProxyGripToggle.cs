using System;
using System.Collections.Generic;
using Kingmaker.Items; // GripType
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // WeaponSetVM
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A grip toggle for the active weapon set — a discoverable button (vs a hidden shortcut) reading
    /// "Grip, one-handed/two-handed"; Enter flips it via <see cref="WeaponSetVM.TryToggleGrip"/>. Reads the
    /// current set live, and is only focusable when that set's weapon can actually be re-gripped
    /// (CanToggleGrip) — mirroring the game's grip button, which is hidden otherwise.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(PositionAnnouncement))]
    public sealed class ProxyGripToggle : UIElement
    {
        private readonly Func<WeaponSetVM> _current;

        public ProxyGripToggle(Func<WeaponSetVM> current) { _current = current; }

        private WeaponSetVM Set => _current?.Invoke();

        public override bool CanFocus { get { var s = Set; return s != null && s.CanToggleGrip.Value; } }

        private string GripText()
        {
            switch (Set?.Grip.Value)
            {
                case GripType.OneHanded: return "one-handed";
                case GripType.TwoHanded: return "two-handed";
                default: return "";
            }
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Localized("ui", "inventory.grip"));
            yield return new RoleAnnouncement("button");
            yield return new ValueAnnouncement(Message.Raw(GripText()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "inventory.toggle_grip"),
                _ => Set?.TryToggleGrip());
        }
    }
}
