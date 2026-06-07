using Kingmaker;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlotEmpty
using WrathAccess.Localization;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game (exploration) context as a navigable screen. It's UNFOCUSED by default — the overlay
    /// owns the arrows for spatial navigation — and <b>Tab enters the HUD</b>, then Tab cycles its regions
    /// (Tab off the end returns to exploration). Regions, in order: the <b>Action bar</b> (the selected
    /// unit's ability/spell/item slots) and the <b>Log</b> button (opens <see cref="ModLogScreen"/>).
    /// Party/combat-turn-order regions come later; combat will vary the set.
    ///
    /// Nothing about the action bar is cached: it's rebuilt every frame while unfocused (the game rapidly
    /// repopulates the slots as it swaps the selected character), and each slot's content/state is read live
    /// from the underlying mechanic by the proxy. While focused we don't rebuild (it would disrupt
    /// navigation), but the proxies still read live so state stays current.
    /// </summary>
    public sealed class InGameScreen : Screen
    {
        public override string Key => "ctx.ingame";
        public override string ScreenName => "Game";
        public override int Layer => 0;
        public override bool StartUnfocused => true; // exploration owns the arrows; Tab brings up the HUD
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsInGame ?? false;

        public override void OnPush() => Build();
        public override void OnPop() => Clear();

        private UIElement _watchedSlot;
        private string _watchedToggle;

        public override void OnUpdate()
        {
            WatchToggle();
            if (Navigation.HasFocus) return; // can't rebuild under the user (the proxies read live anyway)
            Build();
        }

        // While a toggle slot is focused, announce when its state (on / off / targeting) actually changes —
        // this reflects the SETTLED state, including the game's async revert (e.g. Saddle Up flips to
        // targeting then reverts to off with no mount). Baseline (silent) on each new focus; the focus
        // announcement already speaks the initial state. Only toggles are watched (ToggleStateKey is null
        // otherwise), so cooldowns/charges don't chatter.
        private void WatchToggle()
        {
            var slot = Navigation.Active?.Current as ProxyActionBarSlot;
            if (slot == null) { _watchedSlot = null; _watchedToggle = null; return; }
            string state = slot.ToggleStateKey;
            if (!ReferenceEquals(slot, _watchedSlot)) { _watchedSlot = slot; _watchedToggle = state; return; }
            if (state != _watchedToggle)
            {
                _watchedToggle = state;
                if (state != null)
                    Tts.Speak(LocalizationManager.GetOrDefault("ui", state, state), interrupt: false);
            }
        }

        private void Build()
        {
            Clear();
            var vm = ActionBar();

            // Action bar first, then the Log button. Each is its own Tab-stop (the screen is a Panel).
            var bar = new ListContainer("Action bar");
            if (vm != null)
                foreach (var slot in vm.Slots)
                {
                    var m = slot?.MechanicActionBarSlot;
                    if (m != null && !(m is MechanicActionBarSlotEmpty) && !m.IsBad())
                        bar.Add(new ProxyActionBarSlot(slot));
                }
            if (bar.Children.Count == 0) bar.Add(new TextElement("No actions."));
            Add(bar);

            Add(new ProxyActionButton("Log", () => true, ModLogScreen.Open));
        }

        private static ActionBarVM ActionBar()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.ActionBarVM;
        }
    }
}
