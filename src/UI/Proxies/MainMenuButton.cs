using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._VM.ContextMenu;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Builds a <see cref="ProxyActionButton"/> for a main-menu sidebar entry (Continue / New Game / …).
    /// A sidebar entry is just label + enabled + run-command, so it's a plain button; the only twists
    /// are read here: enabled comes from the LIVE <see cref="ContextMenuCollectionEntity.IsEnabled"/>
    /// (which re-invokes the entry's Condition each call) rather than the VM's cached IsEnabled reactive
    /// (stale until RefreshEnabling), and a separator entry isn't focusable.
    /// </summary>
    public static class MainMenuButton
    {
        // The live model behind the VM's cached IsEnabled reactive.
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(ContextMenuEntityVM), "m_Entity");

        public static ProxyActionButton For(ContextMenuEntityVM vm)
        {
            var entity = EntityField?.GetValue(vm) as ContextMenuCollectionEntity;
            Func<bool> enabled = () => entity != null ? entity.IsEnabled : (vm != null && vm.IsEnabled.Value);
            return new ProxyActionButton(() => vm?.Title ?? "", enabled, () => vm?.Execute(),
                canFocus: () => vm != null && !vm.IsSeparator);
        }
    }
}
