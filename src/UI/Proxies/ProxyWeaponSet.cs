using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.Common; // UIUtility.ArabicToRoman
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // WeaponSetVM, EquipSlotVM
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One weapon set (<see cref="WeaponSetVM"/>) as a radio cell in the equipment panel's "Weapon sets"
    /// bar — "Weapon set II: Longsword, Shield". Enter makes the set active (the game's selection contract,
    /// which re-equips its weapons). Grip is its own dedicated button (<see cref="ProxyGripToggle"/>) for
    /// the active set, so it isn't hidden on a shortcut. Mirrors WeaponSetPCView's roman-numeral index +
    /// primary/secondary hands.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(PositionAnnouncement))]
    public sealed class ProxyWeaponSet : UIElement
    {
        private readonly WeaponSetVM _vm;

        public ProxyWeaponSet(WeaponSetVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private static string Weapon(EquipSlotVM s)
            => s != null && s.HasItem ? (s.DisplayName.Value ?? s.Item.Value?.Name) : null;

        private string Name()
        {
            var weapons = new[] { Weapon(_vm.Primary), Weapon(_vm.Secondary) }.Where(w => !string.IsNullOrEmpty(w)).ToArray();
            var set = "Weapon set " + UIUtility.ArabicToRoman(_vm.Index + 1);
            return weapons.Length == 0 ? set + ": empty" : set + ": " + string.Join(", ", weapons);
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_vm.IsSelected.Value);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ => _vm.SetSelectedFromView(true));
        }
    }
}
