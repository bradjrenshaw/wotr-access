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
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map (global map) base context, graph-native. Browse a Tab-stop location list (arrow,
    /// Enter selects), the isolated <see cref="GlobalMapScanner"/> review cursor (PageUp/Down + b/m/n +
    /// . , armies), and the free movement cursor (WASD). Selecting a node (Enter / I / a list item →
    /// <see cref="GlobalMapActions.Go"/>) triggers the game's REAL node-select, and its location panel
    /// (<see cref="GlobalMapEnterMessageVM"/>) then appears as the FIRST <b>tab stop</b> here —
    /// description + Travel/Enter/Manage/Close — which the player tabs to and acts on. The game
    /// disposes+recreates that VM on open, so the panel is location-keyed with a short grace before
    /// dropping (the recreate churn doesn't flicker it) and its texts/labels are captured per location
    /// (the actions still resolve the LIVE VM each press).
    /// </summary>
    public sealed class GlobalMapScreen : Screen
    {
        public override string Key => "ctx.globalmap";
        public override string ScreenName => Loc.T("screen.world_map");
        public override int Layer => 0; // base context, like ctx.ingame

        public override bool IsActive() => GlobalMapModel.Active;

        // Starts unfocused: arrows/WASD drive the movement cursor and Tab enters the lists — like the in-game
        // screen. Category order flips with focus. The scanner/review/cursor keys are the SHARED Exploration
        // actions (Route()d to the world-map systems by screen); WorldMap holds only the Escape→menu key.
        public override bool StartUnfocused => true;
        private static readonly IReadOnlyList<InputCategory> Focused = new[] { InputCategory.UI, InputCategory.Exploration, InputCategory.WorldMap, InputCategory.Windows };
        private static readonly IReadOnlyList<InputCategory> Unfocused = new[] { InputCategory.Exploration, InputCategory.WorldMap, InputCategory.UI, InputCategory.Windows };
        // Without control (a world-map book event / dialogue), drop Windows so the service-window hotkeys go
        // dead there too, exactly as they do in an area (see InGameScreen). Exploration/WorldMap/UI stay.
        private static readonly IReadOnlyList<InputCategory> FocusedNoControl = new[] { InputCategory.UI, InputCategory.Exploration, InputCategory.WorldMap };
        private static readonly IReadOnlyList<InputCategory> UnfocusedNoControl = new[] { InputCategory.Exploration, InputCategory.WorldMap, InputCategory.UI };
        public override IReadOnlyList<InputCategory> InputCategories
        {
            get
            {
                bool ctrl = ControlState.HasControl;
                return Navigation.HasFocus ? (ctrl ? Focused : FocusedNoControl)
                                           : (ctrl ? Unfocused : UnfocusedNoControl);
            }
        }

        public override bool AllowsTypeahead => false; // letters are world-map hotkeys (b/m/n, i), not type-ahead

        /// <summary>True while the location panel is up (now its own modal screen —
        /// <see cref="GlobalMapEnterScreen"/>). The world-map cursor + sonar check this and freeze.</summary>
        public static bool PanelActive => GlobalMapEnterScreen.PanelActive;

        // The location list's ORDER, frozen at map entry (nearest-first from where you arrived) — live
        // distance sorting would shuffle the list under the cursor as the traveler moves. The SET still
        // reads live each render; only the ordering is a per-entry presentation choice (as before).
        private List<GlobalMapPointView> _order;

        private bool _wasPaused; // last frame's travel-pause state (announce on the transition)

        public override void OnPush()
        {
            _order = null; _wasPaused = false;
            GlobalMapScanner.Reset(); GlobalMapCursor.Reset(); // the sonar is an overlay system now (resets on overlay exit)
        }
        public override void OnPop() { _order = null; _wasPaused = false; }

        public override void OnUpdate() => SyncTravelPause();

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


        public override void Build(GraphBuilder b)
        {
            if (!GlobalMapModel.Active) return;
            BuildLocations(b); // the location panel is its own modal screen now (GlobalMapEnterScreen)
        }

        private void BuildLocations(GraphBuilder b)
        {
            if (_order == null)
            {
                var from = GlobalMapModel.TravelerPos;
                _order = GlobalMapModel.Locations.OrderBy(p => Geo.Distance(from, p.transform.position)).ToList();
            }
            // Live set ∩ frozen order: locations key by their view, labels/actions read live.
            var live = new HashSet<GlobalMapPointView>(GlobalMapModel.Locations);
            b.BeginStop("locations").PushContext(Loc.T("worldmap.locations"), "list");
            int i = 0;
            foreach (var p in _order)
            {
                if (p == null || !live.Contains(p)) { i++; continue; }
                var pv = p; // capture per-iteration for the closures
                b.AddItem(ControlId.Referenced(pv, "loc:" + i), GraphNodes.Button(
                    () => GlobalMapActions.Label(pv), () => GlobalMapActions.Go(pv)));
                i++;
            }
            b.PopContext();
        }

        // Escape opens the game menu (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }
    }
}
