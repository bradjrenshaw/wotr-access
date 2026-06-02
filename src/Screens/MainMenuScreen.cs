using Kingmaker;
using Kingmaker.UI.MVVM;
using Kingmaker.UI.MVVM._VM.ContextMenu;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The main menu, our first navigable screen. Its root is a vertical list of
    /// the sidebar entries (Continue / New Game / Load / …) built from
    /// MainMenuSideBarVM, so the navigator can arrow through them and confirm to
    /// run each entry's command — letting a blind player start/load a game with
    /// our own nav and unlock the downstream screens.
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        public override string Key => "ctx.mainmenu";
        public override int Layer => 0;
        // No ScreenName: the sidebar lives in a labeled "Main Menu" container, so the
        // navigator announces it via the focus-path diff instead of the screen self-announcing.

        public override bool IsActive()
        {
            var g = Game.Instance;
            if (g == null || g.RootUiContext == null || !g.RootUiContext.IsMainMenu) return false;

            // The sidebar is hidden whenever a main-menu sub-window covers it
            // (New Game setup, character generation, DLC manager, marketing popup).
            // Until those get their own screens, just stop being navigable here.
            var mm = g.RootUiContext.MainMenuVM;
            if (mm == null) return false;
            if (mm.NewGameVM != null) return false;
            if (mm.DLCManagerVM != null) return false;
            if (mm.CharGenContextVM != null && mm.CharGenContextVM.CharGenVM != null
                && mm.CharGenContextVM.CharGenVM.Value != null) return false;
            if (mm.MarketingMessageVM != null && mm.MarketingMessageVM.Value != null) return false;
            return true;
        }

        public override void OnPush()
        {
            Clear();
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null)
            {
                Main.Log?.Error("MainMenuScreen: sidebar VM was null at OnPush.");
                return;
            }

            // Sidebar entries live in a labeled list, so focusing into it announces
            // "Main Menu" (the container) then the first entry — exercising the path diff.
            var list = new ListContainer("Main Menu");
            list.Add(MainMenuButton.For(sidebar.ContinueVm));
            list.Add(MainMenuButton.For(sidebar.NewGameVm));
            list.Add(MainMenuButton.For(sidebar.LoadVm));
            list.Add(MainMenuButton.For(sidebar.DLCManagerVm));
            list.Add(MainMenuButton.For(sidebar.OptionsVm));
            list.Add(MainMenuButton.For(sidebar.CreditVm));
            list.Add(MainMenuButton.For(sidebar.LicenseVm));
            list.Add(MainMenuButton.For(sidebar.ExitVm));
            Add(list);
        }

        public override void OnPop() => Clear();
    }
}
