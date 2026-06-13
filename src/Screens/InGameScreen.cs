using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized TB control names)
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.Common; // UIUtility.GetServiceWindowsLabel
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlotEmpty
using Kingmaker.UnitLogic; // GetRider / IsSummoned extensions
using Kingmaker.UnitLogic.Parts; // UnitPartInsideAnotherCreature
using WrathAccess.Exploration; // CombatMode
using WrathAccess.Localization;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game (exploration) context as a navigable screen. It's UNFOCUSED by default — the overlay
    /// owns the arrows for spatial navigation — and <b>Tab enters the HUD</b>, then Tab cycles its regions
    /// (Tab off the end returns to exploration). Regions, in order: the <b>Action bar</b> (the selected
    /// unit's ability/spell/item slots), the <b>Menu</b> list (Log → <see cref="ModLogScreen"/>, Rest,
    /// and eventually the rest of the game's compass-corner cluster), and the <b>Windows</b> list.
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
        public override string ScreenName => Loc.T("screen.game");
        public override int Layer => 0;
        public override bool StartUnfocused => true; // exploration owns the arrows; Tab brings up the HUD
        public override bool AllowsTypeahead => false; // letters stay exploration hotkeys (scanner, status…)

        // Both categories are always live in-game; HUD focus decides which one wins SHARED chords
        // (arrows, Enter, Space, Escape, Backspace). Focused: UI first — arrows navigate the HUD —
        // while unshared exploration keys (Shift+arrows, scanner, party) keep working underneath.
        // Unfocused: Exploration first — arrows move the cursor — while unshared UI keys (Tab) still
        // reach the navigator, which is how Tab ENTERS the HUD.
        private static readonly WrathAccess.Input.InputCategory[] FocusedCats =
            { WrathAccess.Input.InputCategory.UI, WrathAccess.Input.InputCategory.Exploration };
        private static readonly WrathAccess.Input.InputCategory[] UnfocusedCats =
            { WrathAccess.Input.InputCategory.Exploration, WrathAccess.Input.InputCategory.UI };
        public override System.Collections.Generic.IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories
            => WrathAccess.UI.Navigation.HasFocus ? FocusedCats : UnfocusedCats;
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsInGame ?? false;

        public override void OnPush() { BuildShell(); _sig = null; _turnSig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _bar = null; _turn = null; }

        private FlowSheet _bar;        // the action-bar FlowSheet (a stable child; only its regions rebuild)
        private ListContainer _turn;   // turn-based Turn panel (a stable child; empty out of combat)
        private string _sig;          // last action-bar content signature
        private string _turnSig;      // last Turn-panel membership signature
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
            RefreshTurn();
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
            _bar = new FlowSheet(Loc.T("hud.action_bar"));
            Add(_bar);

            // The turn-based Turn panel: a stable tab-stop right after the action bar — one vertical list
            // with the status line and controls (End turn, Five foot step) at the top and the initiative
            // order beneath (Enter on a unit = delay your turn until after them). Populated only in
            // turn-based combat (RefreshTurn); while empty it has no focusable children, so the Tab cycle
            // skips it entirely.
            _turn = new ListContainer(Message.Localized("ui", "hud.turn").Resolve());
            Add(_turn);

            // The game's IngameMenu cluster (the compass-corner buttons) as ONE Tab-stop list. Log is
            // ours (the mod's log review); Rest is the game's bonfire button collapsed into one action
            // (see RestAction: gates + camp placement at the cursor + the walk-to-camp interaction;
            // refusals are the game's own warnings, spoken by WarningReader). More cluster functions
            // (skip time, formation…) join here later.
            var menu = new ListContainer(Loc.T("hud.menu"));
            menu.Add(new ProxyActionButton(Loc.T("hud.log"), () => true, ModLogScreen.Open));
            menu.Add(new ProxyActionButton(
                () => TextUtil.StripRichText(UIStrings.Instance.InGameMenuTexts.RestText),
                () => true,
                RestAction.TryRest));
            // The game's gear button: opens the pause/Escape menu (EscMenuScreen takes over).
            menu.Add(new ProxyActionButton(Loc.T("hud.game_menu"), () => true,
                () => Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                    delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); }),
                actionVerb: "open"));
            Add(menu);

            // Service-window buttons (the game's bottom bar): one Tab-stop list after Log. Activating one
            // calls the game's own open path (HandleOpenWindowOfType, which creates the menu + toggles the
            // window), labeled with the game's own localized name.
            var windows = new ListContainer(Loc.T("hud.windows"));
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
            if (mainCount == 0) main.Item(new TextElement(Loc.T("hud.no_actions")));
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

        // Rebuild the Turn panel only when its membership changes (turn-based toggling, units joining,
        // dying, or delaying). The status line, button states, and entry labels are all LIVE, so the
        // active marker moving or actions being spent needs no rebuild — and no focus juggling. If a
        // rebuild does land while focus is inside, restore to the same unit's entry (or the same child
        // index for the fixed controls at the top).
        private void RefreshTurn()
        {
            if (_turn == null) return;
            var units = InitiativeUnits();
            var sb = new StringBuilder(CombatMode.InTurnBased ? "tb|" : "off|");
            foreach (var u in units) sb.Append(u.UniqueId).Append('|');
            var sig = sb.ToString();
            if (sig == _turnSig) return;
            _turnSig = sig;

            // Capture focus (a unit identity for entries; a child index for the fixed controls).
            var current = Navigation.Active?.Current;
            var focusedUnit = (current as InitiativeEntry)?.Unit;
            int focusedIndex = (current != null && current.Parent == _turn) ? _turn.IndexOf(current) : -1;

            _turn.Clear();
            if (CombatMode.InTurnBased)
            {
                // Controls/status at the top, initiative order beneath.
                _turn.Add(new TextElement(() => CombatMode.StatusLine()));
                _turn.Add(new ProxyActionButton(Message.Localized("ui", "turn.end").Resolve(),
                    () => true, CombatMode.EndTurn));
                // The game's own localized control name + a live on/off state.
                _turn.Add(new ProxyActionButton(
                    () => (string)UIStrings.Instance.TurnBasedTexts.FiveFeetActionName + ", "
                        + Message.Localized("ui", CombatMode.FiveFootEngaged ? "value.on" : "value.off").Resolve(),
                    () => CombatMode.FiveFootAvailable || CombatMode.FiveFootEngaged,
                    CombatMode.ToggleFiveFoot,
                    suppressActivateSound: true, actionVerb: "toggle"));
                foreach (var u in units) _turn.Add(new InitiativeEntry(u));
            }

            if (focusedUnit == null && focusedIndex < 0) return; // focus wasn't in the panel
            UIElement target = null;
            if (focusedUnit != null)
                foreach (var child in _turn.Children)
                    if (child is InitiativeEntry e && e.Unit == focusedUnit) { target = e; break; }
            if (target == null && focusedIndex >= 0 && _turn.Children.Count > 0)
                target = _turn.Children[System.Math.Min(focusedIndex, _turn.Children.Count - 1)];
            target = target ?? _turn.FirstFocusable();
            // Same unit → silent restore (its live label already reads current); otherwise announce the landing.
            if (target != null) Navigation.Focus(target, announce: (target as InitiativeEntry)?.Unit != focusedUnit);
        }

        // The units in initiative order, mirroring the game's own tracker (InitiativeTrackerVM.UpdateUnits):
        // SortedUnits is kept sorted by the game (surprise-round actors first, then initiative descending);
        // skip unprepared units and mounts (the rider represents the pair), and hide invisible units unless
        // they're the current unit, summoned, or inside another creature that isn't hidden.
        private static List<UnitEntityData> InitiativeUnits()
        {
            var list = new List<UnitEntityData>();
            if (!CombatMode.InTurnBased) return list;
            var tb = Game.Instance?.TurnBasedCombatController;
            if (tb == null) return list;
            var current = tb.CurrentTurn?.Rider;
            foreach (var u in tb.SortedUnits)
            {
                if (u == null || !u.CombatState.Prepared || u.GetRider() != null) continue;
                if (!u.IsVisibleForPlayer && u != current && !u.IsSummoned())
                {
                    var inside = u.Get<UnitPartInsideAnotherCreature>();
                    if (inside == null || (bool)inside.Owner.State.Features.Hidden) continue;
                }
                list.Add(u);
            }
            return list;
        }

        // One initiative row — live text, so the active marker follows the turn without a rebuild.
        // Enter = delay the acting unit's turn until after this unit (the game's tracker interaction;
        // CombatMode gates it on CanDelay and raises the game's own confirmation modals).
        private sealed class InitiativeEntry : TextElement
        {
            public readonly UnitEntityData Unit;
            public InitiativeEntry(UnitEntityData unit) : base(() => Render(unit)) { Unit = unit; }

            public override IEnumerable<ElementAction> GetActions()
            {
                yield return new ElementAction(ActionIds.Activate,
                    Message.Raw((string)UIStrings.Instance.TurnBasedTexts.DelayTurn),
                    _ => CombatMode.DelayAfter(Unit));
            }

            private static string Render(UnitEntityData u)
            {
                string s = u.CharacterName + ", " + u.CombatState.Initiative;
                if (Game.Instance?.TurnBasedCombatController?.CurrentTurn?.Rider == u)
                    s += ", " + Message.Localized("ui", "combat.active_marker").Resolve();
                return s;
            }
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
