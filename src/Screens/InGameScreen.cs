using Kingmaker;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game (exploration) context as a navigable screen. It's UNFOCUSED by default — the overlay
    /// owns the arrows for spatial navigation — and <b>Tab enters the HUD</b>: a single Tab-stop list of
    /// HUD controls you arrow through (Tab off the end returns to exploration). For now the only control is
    /// the <b>Log</b> button, which opens <see cref="ModLogScreen"/> (channel tabs + the message history).
    /// Action bar / party / combat-turn-order controls come later; combat will vary the set.
    /// </summary>
    public sealed class InGameScreen : Screen
    {
        public override string Key => "ctx.ingame";
        public override string ScreenName => "Game";
        public override int Layer => 0;
        public override bool StartUnfocused => true; // exploration owns the arrows; Tab brings up the HUD
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsInGame ?? false;

        public override void OnPush() { Clear(); Build(); }
        public override void OnPop() => Clear();

        private void Build()
        {
            Clear();
            var hud = new ListContainer(); // the HUD: one Tab-stop, arrow through its controls
            hud.Add(new ProxyActionButton("Log", () => true, ModLogScreen.Open));
            Add(hud);
        }
    }
}
