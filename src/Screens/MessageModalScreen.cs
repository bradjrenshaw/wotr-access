using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.MessageBox;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's generic message/confirm modal (CommonVM.MessageModalVM) — used for the
    /// settings save-changes prompt and confirmations across the whole game. Reads the
    /// message text and exposes the Accept / Decline buttons, activating them via the VM
    /// (OnAcceptPressed / OnDeclinePressed). Layer 30 (above everything else).
    ///
    /// Input-box / item-list modal variants aren't handled yet — only the message + buttons
    /// (the common confirm case).
    /// </summary>
    public sealed class MessageModalScreen : Screen
    {
        public MessageModalScreen() { Wrap = true; } // Tab cycles message ↔ buttons

        public override string Key => "overlay.modal";
        public override string ScreenName => "Dialog";
        public override int Layer => 30;

        public override bool IsActive() => Vm() != null;

        private static MessageModalVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.MessageModalVM.Value
                : null;
        }

        private MessageModalVM _builtFrom;

        public override void OnPush() { _builtFrom = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm != null && vm != _builtFrom)
            {
                // Modal VM swapped (one closed, another opened) — re-home focus.
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            // Message body first (focusable so it can be re-read), then the buttons —
            // all direct children of the root panel, so they're individual Tab-stops.
            if (!string.IsNullOrEmpty(vm.MessageText))
                Add(new TextElement(vm.MessageText));

            Add(new ProxyActionButton(vm.AcceptText, () => true, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline)
                Add(new ProxyActionButton(vm.DeclineText, () => true, () => vm.OnDeclinePressed()));
        }
    }
}
