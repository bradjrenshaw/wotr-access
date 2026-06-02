using System.Collections.Generic;
using WrathAccess.Input;
using WrathAccess.Screens;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A mod key-binding row (one <see cref="InputAction"/>) in the settings menu: announces the action's
    /// label + its current combo. Activate (Enter) opens the capture dialog to rebind; the secondary
    /// action (Backspace) clears it. Clearing re-announces the now-empty value; rebinding is announced by
    /// the capture screen, so we don't re-announce on activate.
    /// </summary>
    public sealed class ProxyModBinding : UIElement
    {
        // Shares the "key binding" settings category + announcement order (see ProxyKeyBindingSlot).
        public override System.Type AnnouncementOrderType => typeof(ProxyKeyBindingSlot);

        private readonly InputAction _action;

        public ProxyModBinding(InputAction action) { _action = action; }

        public override bool ReannounceOnContext => true; // after Clear → read the new "(none)"

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_action.Label));
            yield return new RoleAnnouncement("key binding");
            // Unbound reads the localized "not bound" (shared with ProxyKeyBindingSlot); otherwise the combo.
            yield return new ValueAnnouncement(_action.Bindings.Count == 0
                ? Message.Localized("ui", "value.not_bound") : Message.Raw(_action.BindingsDisplay));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.rebind"),
                _ => ModKeyCaptureScreen.Open(_action));
            yield return new ElementAction(ActionIds.Context, Message.Localized("ui", "action.clear"),
                _ => _action.ClearBindings());
        }
    }
}
