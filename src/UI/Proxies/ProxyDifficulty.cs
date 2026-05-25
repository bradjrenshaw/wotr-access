using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Difficulty;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The game-difficulty picker. It's a dropdown subclass, but each option carries a
    /// description (what that difficulty means), so unlike a plain dropdown the submenu
    /// reads "Title. Description" per option. Left/Right still cycle by name inline.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyDifficulty : UIElement
    {
        private readonly SettingsEntityDropdownGameDifficultyVM _vm;

        public ProxyDifficulty(SettingsEntityDropdownGameDifficultyVM vm) { _vm = vm; }

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        private string CurrentTitle()
        {
            if (_vm == null) return "";
            int i = _vm.GetTempValue();
            var items = _vm.Items;
            if (items != null && i >= 0 && i < items.Count) return items[i].Title;
            return "";
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("combo box");
            yield return new ValueAnnouncement(Message.Raw(CurrentTitle()));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.Raw(_vm?.Description));
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
            var items = _vm?.Items;
            if (items == null || items.Count == 0) return;
            var options = new List<string>(items.Count);
            foreach (var it in items)
            {
                string desc = string.IsNullOrEmpty(it.Description) ? "" : ". " + it.Description;
                options.Add(it.Title + desc);
            }
            ChoiceSubmenuScreen.Open(_vm.Title, options, _vm.GetTempValue(), i => _vm.SetTempValue(i));
        }
    }
}
