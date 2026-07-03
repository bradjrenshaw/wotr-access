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
    /// Text-field modals (e.g. the save overwrite-rename prompt) add an editable name field that opens
    /// our text-entry overlay; Accept reads the field (the VM's OnAcceptPressed passes InputText on).
    /// The item-list modal variant isn't handled yet.
    /// </summary>
    public sealed class MessageModalScreen : Screen
    {
        public MessageModalScreen() { Wrap = true; } // Tab cycles message ↔ buttons

        public override string Key => "overlay.modal";
        public override string ScreenName => Loc.T("screen.dialog");
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

            // Text-field modal (e.g. save overwrite-rename): an editable field. Enter opens our text-entry
            // overlay prefilled with the current value; Accept below submits it (OnAcceptPressed reads InputText).
            if (vm.ModalType == Kingmaker.UI.MessageModalBase.ModalType.TextField)
                Add(new ProxyActionButton(
                    () => Loc.T("modal.edit_name", new { value = vm.InputText.Value }),
                    () => true,
                    () => ModTextEntryScreen.Open(Loc.T("modal.name"), vm.InputText.Value, t => vm.InputText.Value = t),
                    actionVerb: "edit"));

            Add(new ProxyActionButton(vm.AcceptText, () => true, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline)
                Add(new ProxyActionButton(vm.DeclineText, () => true, () => vm.OnDeclinePressed()));
        }
    }
}
