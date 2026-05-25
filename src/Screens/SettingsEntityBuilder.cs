using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Decorative;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Difficulty;
using Owlcat.Runtime.UI.MVVM;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Builds nav elements from a settings-entity collection (the SettingsEntity* VMs), grouping
    /// runs of entities under their headers into labeled section panels. Shared by the Settings
    /// screen and the New Game difficulty phase, which are built from the same VM types.
    /// </summary>
    internal static class SettingsEntityBuilder
    {
        public static void BuildInto(Container content, IEnumerable<VirtualListElementVMBase> entities)
        {
            if (entities == null) return;
            Panel section = null;
            foreach (var e in entities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    section = new Panel(header.Tittle);
                    content.Add(section);
                    continue;
                }
                var proxy = MakeProxy(e);
                if (proxy != null) (section != null ? (Container)section : content).Add(proxy);
            }
        }

        public static UIElement MakeProxy(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return new ProxyToggle(b);
            if (e is SettingsEntitySliderVM s) return new ProxySlider(s);
            if (e is SettingsEntityDropdownGameDifficultyVM diff) return new ProxyDifficulty(diff); // subclass — check before generic dropdown
            if (e is SettingsEntityDropdownVM d) return new ProxyDropdown(d);
            if (e is SettingEntityKeyBindingVM kb) return BuildKeyBindingBar(kb);
            // Privacy/data opt-out: a button that opens the privacy page in a browser.
            if (e is SettingsEntityStatisticsOptOutVM opt) return new ProxyActionButton(opt.Title, () => true, () => opt.OpenSettingsInBrowser());
            if (e is SettingsEntityVM sv) return new ProxyUnsupportedSetting(sv.Title);
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
