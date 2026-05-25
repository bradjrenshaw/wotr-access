using Kingmaker;
using Kingmaker.Settings;
using Kingmaker.UI.MVVM._VM.Settings;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Decorative;
using Owlcat.Runtime.UI.MVVM;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The settings window (shared CommonVM screen — also covers the in-game pause
    /// menu's options). Tree: root Panel = [tabs List, CurrentTab Panel of per-header
    /// section sub-panels, action buttons]. The CurrentTab content is rebuilt when the
    /// tab changes (poll-detected); the tabs/actions stay stable so tab-list focus
    /// survives the rebuild.
    /// </summary>
    public sealed class SettingsScreen : Screen
    {
        public SettingsScreen() { Wrap = true; } // Tab wraps around the whole dialog

        public override string Key => "overlay.settings";
        public override string ScreenName => "Settings";
        public override int Layer => 25;

        public override bool IsActive() => Vm() != null;

        private SettingsVM _builtFrom;
        private object _lastTab;
        private Panel _content;

        private static SettingsVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.SettingsVM.Value
                : null;
        }

        public override void OnPush() { _builtFrom = null; _lastTab = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; _content = null; }

        // Back (Escape) closes the settings window. (Close prompts a save dialog if there
        // are unconfirmed changes — that modal isn't navigable yet.)
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => vm.Close());
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm != _builtFrom)
            {
                // Rare: settings VM swapped while open (e.g. locale/font apply). Re-home focus.
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
                return;
            }
            if (!ReferenceEquals(vm.SelectedMenuEntity.Value, _lastTab))
                RebuildContent(vm);
        }

        private void Rebuild()
        {
            Clear();
            _content = null;
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            var tabs = new ListContainer("Tabs");
            foreach (var tab in vm.SelectionGroup.EntitiesCollection)
                tabs.Add(new ProxySettingsTab(tab, vm));
            Add(tabs);

            _content = new Panel();
            Add(_content);
            RebuildContent(vm);

            // Action buttons are direct children of the root panel, so they're individual
            // Tab-stops you tab through (like a Windows dialog), not an arrow-list.
            Add(new ProxyActionButton("Apply", SettingsController.HasUnconfirmedSettings, () => vm.ApplyAndClose()));
            Add(new ProxyActionButton("Close", () => true, () => vm.Close()));
        }

        // Refills only the content panel (tabs/actions stay put), so focus on the tab
        // list survives a tab switch.
        private void RebuildContent(SettingsVM vm)
        {
            _lastTab = vm.SelectedMenuEntity.Value;
            if (_content == null) return;
            _content.Clear();

            Panel section = null;
            foreach (var e in vm.SettingEntities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    section = new Panel(header.Tittle);
                    _content.Add(section);
                    continue;
                }
                var proxy = MakeProxy(e);
                if (proxy != null) (section != null ? (UI.Container)section : _content).Add(proxy);
            }
        }

        private static UIElement MakeProxy(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return new ProxyToggle(b);
            if (e is SettingsEntitySliderVM s) return new ProxySlider(s);
            if (e is SettingsEntityDropdownVM d) return new ProxyDropdown(d);
            if (e is SettingEntityKeyBindingVM kb) return BuildKeyBindingBar(kb);
            if (e is SettingsEntityVM sv) return new ProxyUnsupportedSetting(sv.Title); // difficulty/opt-out — later
            return null;
        }

        // A key-binding row = a horizontal Bar (labeled with the control) holding its two
        // binding slots; Left/Right moves between them, primary rebinds, secondary clears.
        private static Bar BuildKeyBindingBar(SettingEntityKeyBindingVM vm)
        {
            var bar = new Bar(vm.Title);
            bar.Add(new ProxyKeyBindingSlot(vm, 0, "binding 1"));
            bar.Add(new ProxyKeyBindingSlot(vm, 1, "binding 2"));
            return bar;
        }
    }
}
