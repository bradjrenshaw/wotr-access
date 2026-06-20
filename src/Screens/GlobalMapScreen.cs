using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.Blueprints; // BlueprintGlobalMapPoint
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapEnterMessageVM
using UnityEngine;
using WrathAccess.Exploration; // GlobalMapModel, GlobalMapActions, GlobalMapScanner, GlobalMapEnterPanel, Geo
using WrathAccess.Input; // InputCategory
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map (global map) base context. Browse a Tab-stop location list (arrow, Enter selects), the
    /// isolated <see cref="GlobalMapScanner"/> review cursor (PageUp/Down + b/m/n + . , armies), and the free
    /// movement cursor (WASD). Selecting a node (Enter / I / a list item → <see cref="GlobalMapActions.Go"/>)
    /// triggers the game's REAL node-select, and its location panel (<see cref="GlobalMapEnterMessageVM"/>)
    /// then appears as an extra <b>tab stop</b> here — description + Travel/Enter/Manage/Close — which the
    /// player tabs to and acts on. (Earlier this was a separate modal; the game disposes+recreates that VM on
    /// open, which fought a modal's focus, so it's a passive tab stop instead — added/removed in place,
    /// location-keyed with a short grace so the recreate churn doesn't flicker it.)
    /// </summary>
    public sealed class GlobalMapScreen : Screen
    {
        public override string Key => "ctx.globalmap";
        public override string ScreenName => Loc.T("screen.world_map");
        public override int Layer => 0; // base context, like ctx.ingame

        public override bool IsActive() => GlobalMapModel.Active;

        // Starts unfocused: arrows/WASD drive the movement cursor and Tab enters the lists — like the in-game
        // screen. Category order flips with focus; the scanner/review/cursor letter+page keys are WorldMap-only.
        public override bool StartUnfocused => true;
        private static readonly IReadOnlyList<InputCategory> Focused = new[] { InputCategory.UI, InputCategory.WorldMap };
        private static readonly IReadOnlyList<InputCategory> Unfocused = new[] { InputCategory.WorldMap, InputCategory.UI };
        public override IReadOnlyList<InputCategory> InputCategories => Navigation.HasFocus ? Focused : Unfocused;

        public override bool AllowsTypeahead => false; // letters are world-map hotkeys (b/m/n, i), not type-ahead

        /// <summary>True while the location panel tab stop is shown (grace-stable). The world-map cursor +
        /// sonar system check this and freeze, so they don't run while the player reads/acts on the panel.</summary>
        public static bool PanelActive { get; private set; }

        private bool _built;
        private ListContainer _panel;              // the location-panel tab stop (null = none open)
        private BlueprintGlobalMapPoint _panelLoc; // the location it was built for
        private float _panelClearAt;               // grace deadline before dropping the panel after its VM vanished
        private bool _wasPaused;                   // last frame's travel-pause state (announce on the transition)

        public override void OnPush()
        {
            _built = false; ClearPanelState();
            GlobalMapScanner.Reset(); GlobalMapCursor.Reset(); // the sonar is an overlay system now (resets on overlay exit)
        }
        public override void OnPop() { Clear(); _built = false; ClearPanelState(); }

        private void ClearPanelState() { _panel = null; _panelLoc = null; _panelClearAt = 0f; PanelActive = false; _wasPaused = false; }

        public override void OnUpdate()
        {
            if (!_built && GlobalMapModel.Active) { _built = true; Rebuild(); }
            if (_built) SyncPanel();
            SyncTravelPause();
        }

        // The game pauses travel mid-journey on a discovery/event (its move-helper shows Continue). Announce
        // the pause once on the transition so the player knows to resume (Enter on the cursor → resume); the
        // discovery line itself is read by the Log overlay system. Skip when a location panel is up (a user
        // select also pauses travel, but the panel already has focus).
        private void SyncTravelPause()
        {
            bool paused = GlobalMapModel.TravelPaused;
            if (paused && !_wasPaused && !PanelActive) Tts.Speak(Loc.T("worldmap.travel_paused"));
            _wasPaused = paused;
        }

        private void Rebuild()
        {
            Clear();
            var from = GlobalMapModel.TravelerPos;
            var list = new ListContainer(Loc.T("worldmap.locations"));
            foreach (var p in GlobalMapModel.Locations.OrderBy(p => Geo.Distance(from, p.transform.position)))
            {
                var pv = p; // capture per-iteration for the closures
                list.Add(new ProxyActionButton(() => GlobalMapActions.Label(pv), () => true, () => GlobalMapActions.Go(pv)));
            }
            if (list.Children.Count > 0) Add(list);
            Navigation.Attach(this);
        }

        // ---- the location panel, as a tab stop synced to the game's live GlobalMapEnterMessageVM ----

        private static GlobalMapEnterMessageVM PanelVm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapEnterMessageVM?.Value;
        }

        // Add/drop the panel tab stop as the game's panel VM comes and goes — WITHOUT touching the location
        // list (so its focus survives). Keyed on the LOCATION so the game's open-time dispose+recreate churn
        // (same location) doesn't rebuild it; a short grace before dropping absorbs the transient nulls.
        private void SyncPanel()
        {
            var vm = PanelVm();
            var loc = vm != null && vm.Location != null ? vm.Location.Blueprint : null;
            if (loc != null)
            {
                _panelClearAt = 0f;
                if (loc != _panelLoc) SetPanel(loc, vm);
                return;
            }
            if (_panelLoc == null) return;
            if (_panelClearAt == 0f) _panelClearAt = Time.unscaledTime + 0.25f;
            else if (Time.unscaledTime >= _panelClearAt) SetPanel(null, null);
        }

        private void SetPanel(BlueprintGlobalMapPoint loc, GlobalMapEnterMessageVM vm)
        {
            if (_panel != null) { Remove(_panel); _panel = null; }
            _panelLoc = loc;
            _panelClearAt = 0f;
            PanelActive = loc != null;
            if (loc == null || vm == null) return;
            _panel = BuildPanel(vm);
            Insert(0, _panel); // FIRST tab stop, so Tab from the map lands on the panel directly when it's open
            Tts.Speak(Loc.T("worldmap.selected", new { name = GlobalMapEnterPanel.Title(vm) })); // signal it's there
        }

        private static ListContainer BuildPanel(GlobalMapEnterMessageVM vm)
        {
            var list = new ListContainer(Loc.T("worldmap.panel")); // header announces "Location options" on Tab-in

            // Location lore first (what the place is), then the game-panel body (travel time / enter
            // confirmation / closed or restricted reason). Both are location-stable, captured here.
            var lore = GlobalMapEnterPanel.LocationDescription(vm);
            if (!string.IsNullOrWhiteSpace(lore)) list.Add(new TextElement(() => lore));

            GlobalMapEnterPanel.Compute(vm, out string desc, out bool acceptEnabled);
            if (!string.IsNullOrWhiteSpace(desc)) list.Add(new TextElement(() => TextUtil.StripRichText(desc)));

            // Labels are location-stable (captured); the actions resolve the LIVE VM each press (the instance
            // may be recreated under us), never a stale capture. Each fires the game's button-click sound +
            // the VM method the real OwlcatButton is wired to (Accept/AlternativeAction/Close) — same behavior
            // as pressing the button (see GlobalMapEnterMessagePCView), minus reaching into the live view.
            string acceptLabel = TextUtil.StripRichText(GlobalMapEnterPanel.AcceptLabel(vm));
            list.Add(new ProxyActionButton(() => acceptLabel, () => acceptEnabled, AcceptLive));

            if (GlobalMapEnterPanel.HasSettlement(vm))
            {
                string manageLabel = TextUtil.StripRichText(GlobalMapEnterPanel.ManageLabel());
                list.Add(new ProxyActionButton(() => manageLabel, () => true, () => { PlayClick(); PanelVm()?.AlternativeAction(); }));
            }

            string closeLabel = TextUtil.StripRichText(GlobalMapEnterPanel.CloseLabel());
            list.Add(new ProxyActionButton(() => closeLabel, () => true, () => { PlayClick(); PanelVm()?.Close(); }));
            return list;
        }

        // The default button-click sound the OwlcatButton plays on a left-click (UISoundController, exactly as
        // the game does it), so our VM-driven actions sound identical to a real button press.
        private static void PlayClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();

        // Travel / Enter on the LIVE VM (what the Accept OwlcatButton is wired to), with its click sound.
        // Confirm the outcome for the player case; a selected crusade army gets the game's own "set
        // destination" warning, so stay quiet there to avoid doubling it.
        private static void AcceptLive()
        {
            var vm = PanelVm();
            if (vm == null) return;
            PlayClick();
            var army = Game.Instance.GlobalMapController != null ? Game.Instance.GlobalMapController.SelectedArmy : null;
            bool entering = vm.IsCurrentLocation;
            var name = GlobalMapEnterPanel.Title(vm);
            vm.Accept();
            if (army == null) Tts.Speak(Loc.T(entering ? "worldmap.entering" : "worldmap.traveling", new { name }));
        }

        // Escape opens the game menu (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }
    }
}
