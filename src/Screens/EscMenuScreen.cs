using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.EscMenu;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game pause/Escape menu (<c>CommonVM.EscMenuContextVM.EscMenu</c>): the game's own
    /// ContextMenuEntityVM buttons — quick save/load, save/load, options, photo mode, main menu,
    /// quit, bug report (whichever exist on this platform/build) — reused through
    /// <see cref="MainMenuButton"/>, the same proxy as the main-menu sidebar (live-enabled: greyed
    /// quick load before any quicksave, etc.). Escape closes through the VM's own close action.
    /// Opened the same way the game's gear button opens it (IEscMenuHandler.HandleOpen — see the
    /// HUD Menu entry and the exploration Escape binding); the game's EscMode pause runs underneath.
    /// </summary>
    public sealed class EscMenuScreen : Screen
    {
        public override string Key => "ctx.escmenu";
        public override string ScreenName => Loc.T("screen.game_menu");
        public override int Layer => 24; // over contexts/windows/dialogs; below modals + tutorials

        private EscMenuVM _builtVm;

        private static EscMenuVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.CommonVM?.EscMenuContextVM?.EscMenu?.Value;
        }

        public override bool IsActive() => Vm() != null;

        // Build in OnPush: the screen-change announcement (screen name + first focus) happens right
        // after push — content built lazily in OnUpdate would land focus silently, after the announce.
        public override void OnPush() { _builtVm = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtVm = null; }

        public override void OnUpdate() { Rebuild(); }

        private void Rebuild()
        {
            var vm = Vm();
            if (vm == null || vm == _builtVm) return;
            _builtVm = vm;
            Clear();
            var list = new ListContainer();
            foreach (var entry in new[]
            {
                vm.QuickSaveVm, vm.QuickLoadVm, vm.SaveVm, vm.LoadVm, vm.OptionsVm,
                vm.PhotoModeVm, vm.MainMenuVm, vm.ExitVm, vm.BugReportVm,
            })
                if (entry != null) list.Add(MainMenuButton.For(entry));
            Add(list);
            Navigation.Attach(this);
        }

        // Escape closes the menu via its own close action (the same path the game's X / re-press uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => vm.OnClose());
        }
    }
}
