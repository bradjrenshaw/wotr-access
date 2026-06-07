using Kingmaker;
using Kingmaker.UI.Common; // UIUtility.GetServiceWindowsLabel
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsVM, ServiceWindowsType
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
        private bool _watchedEnabled;

        public override void OnUpdate()
        {
            WatchSlot();
            if (Navigation.HasFocus) return; // can't rebuild under the user (the proxies read live anyway)
            Build();
        }

        // While an action-bar slot is focused, announce when its live state changes under you — the toggle
        // on/off/targeting (incl. the game's async settle/revert, e.g. Saddle Up), and the enabled/disabled
        // gate (e.g. Charge becoming usable once mounted). Baseline (silent) on each new focus; the focus
        // announcement already spoke the initial state. Only the focused slot is watched, so it never chatters.
        private void WatchSlot()
        {
            var slot = Navigation.Active?.Current as ProxyActionBarSlot;
            if (slot == null) { _watchedSlot = null; return; }
            string toggle = slot.ToggleStateKey;
            bool enabled = slot.Enabled;
            if (!ReferenceEquals(slot, _watchedSlot)) { _watchedSlot = slot; _watchedToggle = toggle; _watchedEnabled = enabled; return; }
            if (toggle != _watchedToggle)
            {
                _watchedToggle = toggle;
                if (toggle != null) Tts.Speak(LocalizationManager.GetOrDefault("ui", toggle, toggle), interrupt: false);
            }
            if (enabled != _watchedEnabled)
            {
                _watchedEnabled = enabled;
                Tts.Speak(LocalizationManager.GetOrDefault("ui", enabled ? "state.enabled" : "state.disabled",
                    enabled ? "enabled" : "disabled"), interrupt: false);
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

            // Service-window buttons (the game's bottom bar): one Tab-stop list after Log. Activating one
            // calls the game's own open path (HandleOpenWindowOfType, which creates the menu + toggles the
            // window), labeled with the game's own localized name.
            var windows = new ListContainer("Windows");
            foreach (var type in ServiceButtons)
            {
                var t = type; // capture for the live closures
                windows.Add(new ProxyActionButton(() => UIUtility.GetServiceWindowsLabel(t), () => true,
                    () => ServiceWindows()?.HandleOpenWindowOfType(t), actionVerb: "open"));
            }
            Add(windows);
        }

        // The service windows we expose (in-game). Mythic / Equipment / SmartItem are conditional — added
        // later with their availability checks.
        private static readonly ServiceWindowsType[] ServiceButtons =
        {
            ServiceWindowsType.CharacterInfo, ServiceWindowsType.Inventory, ServiceWindowsType.Spellbook,
            ServiceWindowsType.Journal, ServiceWindowsType.Encyclopedia, ServiceWindowsType.LocalMap,
        };

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.ServiceWindowsVM;
        }

        private static ActionBarVM ActionBar()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.ActionBarVM;
        }
    }
}
