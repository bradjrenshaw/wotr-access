using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Difficulty;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// The game-difficulty picker. It's a dropdown subclass, but each option carries a
    /// description (what that difficulty means), so unlike a plain dropdown the submenu
    /// reads "Title. Description" per option. Like <see cref="ProxyDropdown"/>, it opens via primary
    /// and does NOT advertise Left/Right adjust (those are tree collapse/ascend).
    /// </summary>
    public sealed class ProxyDifficulty : UIElement
    {
        // Shares the "combo box" settings category + announcement order (see ProxyDropdown).
        public override System.Type AnnouncementOrderType => typeof(ProxyDropdown);

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
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ => OpenSubmenu());
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => WrathAccess.UI.Tooltips.SimpleTooltip.Make(_vm?.Title, _vm?.Description);

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
