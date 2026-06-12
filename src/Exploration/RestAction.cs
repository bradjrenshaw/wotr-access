using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickMapObjectHandler, PlaceRestMarkerHandler
using Kingmaker.Controllers.Rest;            // RestHelper
using Kingmaker.View.MapObjects;             // CampPlaceView
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The HUD's Rest button as ONE accessible action. The game's flow is mouse-shaped: the bonfire
    /// button arms a rest-marker POINTER MODE, the player clicks a walkable spot (the camp marker
    /// spawns), then clicks the marker — its RestPart walks the whole party over and the camp UI
    /// opens on arrival. We collapse it: the same gates (RestHelper.TryStartRest — combat and
    /// no-camping warnings, spoken by WarningReader), then place the camp at the nearest valid spot
    /// to the movement cursor (PlaceRestMarkerHandler.OnClick — the game's CanPlace test and its
    /// enemies-nearby warning), then drive the spawned camp's interaction through
    /// ClickMapObjectHandler exactly as if it were clicked.
    /// </summary>
    internal static class RestAction
    {
        private static int _pendingInteract; // frames left to wait for the freshly spawned camp view

        public static void TryRest()
        {
            var game = Game.Instance;
            if (game == null) return;
            if (!RestHelper.TryStartRest()) return; // gates + the game's own spoken warnings
            // TryStartRest armed the rest-marker pointer mode (waiting for a mouse click); we place directly.
            game.ClickEventsController?.ClearPointerMode();

            var handler = game.PlaceRestMarkerHandler;
            var center = Cursor.Has ? Cursor.Position.Value : Overlays.Cursor.PlayerPosition;
            Vector3? spot = FindSpot(handler, center);
            if (spot == null) { Tts.Speak(Loc.T("rest.no_spot"), interrupt: false); return; }

            handler.CursorValid = true; // normally maintained by the mouse-hover validator
            if (!handler.OnClick(null, spot.Value, 0)) return; // enemies nearby → the game's warning
            Tts.Speak(Loc.T("rest.making_camp"), interrupt: false);
            _pendingInteract = 30; // the camp view appears within a frame or two
        }

        // The game's own camp-spot search (QA TaskAreaRest.TryFindCampPosition), centred on our point:
        // an expanding +/- grid in 2m steps, ~10m out, first placeable spot wins.
        private static Vector3? FindSpot(PlaceRestMarkerHandler handler, Vector3 center)
        {
            if (handler == null) return null;
            for (int i = 0; i <= 10; i++)
            {
                float dx = Mathf.Pow(-1f, i) * (i / 2) * 2f;
                for (int j = 0; j <= 10; j++)
                {
                    float dz = Mathf.Pow(-1f, j) * (j / 2) * 2f;
                    var p = new Vector3(center.x + dx, center.y, center.z + dz);
                    if (handler.CanPlace(p)) return p;
                }
            }
            return null;
        }

        /// <summary>Per-frame: once the placed camp's view exists, trigger its interaction — the same
        /// path as clicking the campfire (RestPart gathers the party and walks everyone to camp).</summary>
        public static void Tick()
        {
            if (_pendingInteract <= 0) return;
            var view = CampPlaceView.PlayerPlacedInstance;
            if (view == null) { _pendingInteract--; return; }
            _pendingInteract = 0;

            var sc = Game.Instance?.SelectionCharacter;
            if (sc != null && (sc.SelectedUnits == null || sc.SelectedUnits.Count == 0))
                Game.Instance.UI?.SelectionManager?.SelectAll();
            var units = sc?.SelectedUnits;
            if (units == null || units.Count == 0) return;
            ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions: true);
        }
    }
}
