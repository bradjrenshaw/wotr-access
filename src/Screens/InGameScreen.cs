using System.Collections.Generic;
using System.Text;
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
    /// Nothing about the action bar is cached: the Log/Windows tab-stops are built once, and the action bar
    /// itself is a FlowSheet rebuilt in place whenever its content signature changes — the selected character
    /// swapping, abilities/items appearing or being consumed, etc. — so it refreshes even while focused. A
    /// rebuild restores the cursor to the same grid position (deduping the announce), so it never strands you.
    /// Each slot's live state (on/off, counts) is read by the proxy without needing a rebuild.
    /// </summary>
    public sealed class InGameScreen : Screen
    {
        public override string Key => "ctx.ingame";
        public override string ScreenName => "Game";
        public override int Layer => 0;
        public override bool StartUnfocused => true; // exploration owns the arrows; Tab brings up the HUD
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsInGame ?? false;

        public override void OnPush() { BuildShell(); _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _bar = null; }

        private FlowSheet _bar;        // the action-bar FlowSheet (a stable child; only its regions rebuild)
        private string _sig;          // last action-bar content signature
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle
        private UIElement _watchedSlot;
        private string _watchedToggle;
        private bool _watchedEnabled;

        public override void OnUpdate()
        {
            WatchSlot();
            if (_bar == null) BuildShell();
            var sig = Sig();
            if (sig != _sig) { _sig = sig; RefreshBar(); }
            else _lastRestoreLabel = null; // settled: the next change announces its landing
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

        // Build the stable HUD shell once: the action-bar FlowSheet (populated separately), then the Log
        // button and the service-window buttons (which never change), so their focus survives a bar refresh.
        private void BuildShell()
        {
            Clear();
            _bar = new FlowSheet("Action bar");
            Add(_bar);

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

            PopulateBar();
        }

        // (Re)fill the action-bar FlowSheet in place: the main slot list, then the game's Spell / Ability /
        // Item groups as collapsible regions (collapsed = a header button; expanded = every spell/ability/item
        // the unit has — the source the game drags onto the bar, so e.g. Charge is reachable + usable here).
        private void PopulateBar()
        {
            _bar.ClearRegions();
            var vm = ActionBar();
            var main = _bar.List(null); // the sheet is already "Action bar"; the groups below carry their own labels
            int mainCount = 0;
            if (vm != null)
                foreach (var slot in vm.Slots)
                    if (Usable(slot)) { main.Item(new ProxyActionBarSlot(slot)); mainCount++; }
            if (mainCount == 0) main.Item(new TextElement("No actions."));
            AddGroup(_bar, vm?.GroupAbilities, "hud.abilities");
            AddGroup(_bar, vm?.GroupSpells, "hud.spells");
            AddGroup(_bar, vm?.GroupItems, "hud.items");
            _bar.Reflow();
        }

        // Rebuild the bar, keeping the cursor on the same grid position if it was in the bar (so a character
        // swap / item use under you doesn't strand focus). Focus on Log/Windows is untouched.
        private void RefreshBar()
        {
            int row = -1, col = 0;
            var cur = Navigation.Active?.Current;
            if (cur != null && _bar.TryCoords(cur, out int r, out int c)) { row = r; col = c; }
            PopulateBar();
            if (row >= 0) RestoreBarFocus(row, col);
        }

        private void RestoreBarFocus(int row, int col)
        {
            if (_bar.RowCount == 0) return;
            int r = System.Math.Min(row, _bar.RowCount - 1);
            int c = _bar.Visitable(r, col) ? col : _bar.LeftmostVisitable(r);
            var cell = c >= 0 ? _bar.CellAt(r, c) : _bar.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel; // suppress the repeat while a change settles over frames
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }

        // The action bar's content fingerprint: the selected unit + the titles of the usable slots in each
        // group. Changes when a character is swapped, abilities/items appear, or an item is consumed — which
        // is exactly when we must rebuild. Live per-slot state (counts, on/off) is read by the proxy, so it
        // doesn't need to be in here.
        private static string Sig()
        {
            var sb = new StringBuilder();
            sb.Append(Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value?.CharacterName).Append('|');
            var vm = ActionBar();
            if (vm != null)
            {
                AppendSlots(sb, vm.Slots);
                AppendSlots(sb, vm.GroupAbilities);
                AppendSlots(sb, vm.GroupSpells);
                AppendSlots(sb, vm.GroupItems);
            }
            return sb.ToString();
        }

        private static void AppendSlots(StringBuilder sb, List<ActionBarSlotVM> slots)
        {
            sb.Append('|');
            if (slots == null) return;
            foreach (var s in slots)
                if (Usable(s)) sb.Append(s.MechanicActionBarSlot.GetTitle()).Append(',');
        }

        // A real, usable action-bar slot: backed by a non-empty, non-bad mechanic.
        private static bool Usable(ActionBarSlotVM slot)
        {
            var m = slot?.MechanicActionBarSlot;
            return m != null && !(m is MechanicActionBarSlotEmpty) && !m.IsBad();
        }

        // One collapsible group (Abilities / Spells / Items): the group's usable slots under a folding header.
        private void AddGroup(FlowSheet sheet, System.Collections.Generic.List<ActionBarSlotVM> slots, string labelKey)
        {
            if (slots == null) return;
            CollapsibleRegion region = null;
            foreach (var slot in slots)
                if (Usable(slot))
                {
                    if (region == null) region = sheet.Collapsible(Message.Localized("ui", labelKey).Resolve());
                    region.Item(new ProxyActionBarSlot(slot));
                }
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
