using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._VM.ContextMenu;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Wraps a main-menu sidebar entry (ContextMenuEntityVM). Announces its label,
    /// an "unavailable" status when disabled, the "button" role, and its position.
    /// Confirm runs the entry's command.
    ///
    /// Enabled state reads the live <see cref="ContextMenuCollectionEntity.IsEnabled"/>
    /// (which re-invokes its Condition each call) rather than the VM's cached IsEnabled
    /// reactive — the cache only updates on RefreshEnabling and goes stale otherwise.
    /// </summary>
    // Screen-reader convention: name, role, value, enabled-state, position.
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyMainMenuButton : UIElement
    {
        // The live model behind the VM's cached IsEnabled reactive, extracted once.
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(ContextMenuEntityVM), "m_Entity");

        private readonly ContextMenuEntityVM _vm;
        private readonly ContextMenuCollectionEntity _entity;

        public ProxyMainMenuButton(ContextMenuEntityVM vm)
        {
            _vm = vm;
            _entity = EntityField?.GetValue(vm) as ContextMenuCollectionEntity;
        }

        public override bool CanFocus => _vm != null && !_vm.IsSeparator;

        // Live (never stale): re-invokes the entry's Condition. Falls back to the
        // cached reactive only if the private field couldn't be resolved.
        private bool IsEntryEnabled() =>
            _entity != null ? _entity.IsEnabled : (_vm != null && _vm.IsEnabled.Value);

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("button");
            yield return new EnabledAnnouncement(IsEntryEnabled());
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (IsEntryEnabled())
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Activate"), _ => _vm.Execute());
        }
    }
}
