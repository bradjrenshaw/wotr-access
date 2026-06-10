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
        /// <param name="tree">When true, header runs become collapsible <see cref="TreeGroup"/>s (a
        /// treeview — one Tab-stop, Down/Up over expanded controls, Right/Left expand/collapse). When
        /// false, they're <see cref="Panel"/> sections (each control its own Tab-stop) — the old shape.</param>
        public static void BuildInto(Container content, IEnumerable<VirtualListElementVMBase> entities,
            bool tree = false)
        {
            if (entities == null) return;
            Container section = null;
            foreach (var e in entities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    section = tree ? (Container)new TreeGroup(header.Tittle) : new Panel(header.Tittle);
                    content.Add(section);
                    continue;
                }
                var proxy = MakeProxy(e);
                if (proxy != null) (section ?? content).Add(proxy);
            }
        }

        public static UIElement MakeProxy(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return new ProxyToggle(b);
            if (e is SettingsEntitySliderVM s) return new ProxySlider(s);
            if (e is SettingsEntityDropdownGameDifficultyVM diff) return new ProxyDifficulty(diff); // subclass — check before generic dropdown
            if (e is SettingsEntityDropdownVM d) return new ProxyDropdown(d);
            if (e is SettingEntityKeyBindingVM kb) return BuildKeyBindingGroup(kb);
            // Privacy/data opt-out: a button that opens the privacy page in a browser.
            if (e is SettingsEntityStatisticsOptOutVM opt) return new ProxyActionButton(opt.Title, () => true, () => opt.OpenSettingsInBrowser());
            if (e is SettingsEntityVM sv) return new ProxyUnsupportedSetting(sv.Title);
            return null;
        }

        // A key-binding row = a collapsible tree group (labeled with the control) holding its two
        // binding slots as button children: expand to reach them, primary rebinds, secondary clears.
        // (Matches the treeview — no more horizontal Left/Right bar.)
        private static TreeGroup BuildKeyBindingGroup(SettingEntityKeyBindingVM vm)
        {
            var group = new TreeGroup(vm.Title);
            group.Add(new ProxyKeyBindingSlot(vm, 0, Loc.T("bind.slot", new { index = 1 })));
            group.Add(new ProxyKeyBindingSlot(vm, 1, Loc.T("bind.slot", new { index = 2 })));
            return group;
        }
    }
}
