using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Armies; // ArmyFaction
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Shared world-map verbs + labels, used by both the location list (<see cref="WrathAccess.Screens
    /// .GlobalMapScreen"/>) and the world-map scanner (<see cref="GlobalMapScanner"/>). Travel/enter drives
    /// the game's own <c>GoToLocationRevealed</c>/<c>EnterLocation</c> (the two common branches of the
    /// location panel); labels read name + compass bearing + state. Miles distance / the full multi-option
    /// panel arrive in later increments.
    /// </summary>
    internal static class GlobalMapActions
    {
        /// <summary>A point's spoken name — its location name, or a generic "junction" for the unnamed
        /// road-junction waypoints.</summary>
        public static string Name(GlobalMapPointView p)
        {
            // The war camp's junction has no name of its own: use the game's camp label.
            if (GlobalMapModel.IsWarCamp(p)) return Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.CrusadeTexts.WarCamp;
            var n = (string)p.Blueprint.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("worldmap.junction") : n;
        }

        /// <summary>Name + compass bearing from the party + a state tag (here / closed). For lists.</summary>
        /// <param name="from">The origin for the bearing: the world-map cursor for the review cycles
        /// (cursor-relative, like the in-area scanner), the traveler for the map screen's own list.</param>
        public static string Label(GlobalMapPointView p, Vector3 from)
        {
            var parts = new List<string> { Name(p) };
            if (p.Blueprint == GlobalMapModel.CurrentLocation)
            {
                parts.Add(Loc.T("worldmap.you_are_here"));
            }
            else
            {
                var bearing = Geo.Bearing(from, p.transform.position);
                if (!string.IsNullOrEmpty(bearing)) parts.Add(bearing);
                if (p.State.IsClosed) parts.Add(Loc.T("worldmap.closed"));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Name + state (here / closed), WITHOUT bearing — for "what the cursor is on" readouts
        /// (the analogue of in-area DescribeInPlace), where direction would be noise.</summary>
        public static string InPlace(GlobalMapPointView p)
        {
            var parts = new List<string> { Name(p) };
            if (p.Blueprint == GlobalMapModel.CurrentLocation) parts.Add(Loc.T("worldmap.you_are_here"));
            else if (p.State.IsClosed) parts.Add(Loc.T("worldmap.closed"));
            return string.Join(", ", parts);
        }

        /// <summary>Compass bearing + miles distance from the party to an arbitrary point on the map — the
        /// tiled cursor's readout when a step lands on an EMPTY cell (no point), so each step still places the
        /// cursor for tracking (the tiled mode's purpose). Units == miles on the global map.</summary>
        public static string PositionAt(Vector3 c)
        {
            var party = GlobalMapModel.TravelerPos;
            if (Geo.IsHere(party, c)) return Loc.T("geo.here");
            var bearing = Geo.Bearing(party, c);
            var miles = Geo.MilesStr(Geo.Distance(party, c));
            return string.IsNullOrEmpty(bearing) ? miles : bearing + ", " + miles;
        }

        /// <summary>Select a point — <b>100% the game's own click</b> (<see cref="GlobalMapPointView.HandleClick"/>),
        /// which routes through the global-map input-state machine + the click debounce before selecting, so the
        /// game's state stays consistent. (A hand-rolled <c>InteractWithLocation</c> skipped the state machine and
        /// softlocked the map.) The game's location panel then surfaces accessibly as a tab stop on
        /// <see cref="WrathAccess.Screens.GlobalMapScreen"/>, which announces the selection when it appears. The
        /// view's own guards (revealed / travels-paused / not-in-cutscene) decide whether anything happens.</summary>
        public static void Go(GlobalMapPointView pv)
        {
            if (pv != null) pv.HandleClick();
        }

        /// <summary>Resume travel paused mid-journey — the same <c>StartTravels</c> the game's move-helper
        /// Continue button drives (verified live), with its button-click sound.</summary>
        public static void ResumeTravel()
        {
            Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();
            Game.Instance.GlobalMapController.StartTravels(fromClick: true);
            Tts.Speak(Loc.T("worldmap.continuing"));
        }

        // ---- armies (the . enemy / , ally cycles) ----

        /// <summary>An army's world position — its spawned pawn, else the node it sits on; null while it has
        /// neither (shouldn't happen for a revealed army on the live map).</summary>
        public static Vector3? ArmyPosition(GlobalMapArmyState army)
        {
            if (army == null) return null;
            var view = army.View;
            if (view != null) return view.Position;
            var loc = army.Location;
            if (loc != null && GlobalMapView.Instance != null)
            {
                var pv = GlobalMapView.Instance.GetPointView(loc);
                if (pv != null) return pv.transform.position;
            }
            return null;
        }

        /// <summary>An army's spoken name (e.g. "Crusade Army III" / "Demon Army"), with a generic fallback.</summary>
        public static string ArmyName(GlobalMapArmyState army)
        {
            var n = army != null && army.Data != null ? (string)army.Data.ArmyName : null;
            return string.IsNullOrEmpty(n) ? Loc.T("worldmap.army_fallback") : n;
        }

        /// <summary>The review-ping outcome for a target, by the game's own path manager for the CURRENT
        /// traveler (the selected army, else the party — so army-only / party-only roads and locked roads
        /// count the way the game counts them): "straight" = where you are or one road away, "path" =
        /// several roads, "unreachable" = no path. Exactly one of <paramref name="point"/> /
        /// <paramref name="army"/> is the target.</summary>
        public static string RouteOutcome(GlobalMapPointView point, GlobalMapArmyState army)
        {
            var view = GlobalMapView.Instance;
            var state = view?.State;
            var pm = state?.PathManager;
            if (pm == null) return "unreachable";
            var traveler = Kingmaker.Game.Instance?.GlobalMapController?.SelectedTraveler ?? (IGlobalMapTraveler)state.Player;
            var selectedArmy = traveler as GlobalMapArmyState;
            GlobalMapTravelData data = null;
            if (point != null && point.Blueprint != null)
                data = pm.CalculateTravelerPathToLocation(traveler, point.Blueprint);
            else if (army != null)
                data = selectedArmy != null ? pm.CalculateArmyPathToPosition(selectedArmy, army.Position)
                                            : pm.CalculatePlayerPathToPosition(army.Position);
            if (data == null) return "unreachable";
            return data.Path.Count <= 1 ? "straight" : "path";
        }

        /// <summary>Name + side (ally/enemy), WITHOUT bearing — for "what the cursor is on" readouts (the
        /// army analogue of <see cref="InPlace"/>).</summary>
        public static string ArmyInPlace(GlobalMapArmyState army)
            => ArmyName(army) + ", " + Loc.T(army.Data.Faction == ArmyFaction.Crusaders ? "worldmap.army_ally" : "worldmap.army_enemy");

        /// <summary>Act on an army (the review cursor's i, the movement cursor's Enter): your own army is
        /// the pawn click — select it (army mode), with the game's select sound; the screen announces the
        /// change of traveler. Already selected, or an enemy: read it.</summary>
        public static void ArmyInteract(GlobalMapArmyState army)
        {
            var controller = Kingmaker.Game.Instance?.GlobalMapController;
            if (controller != null && army.Data.Faction == ArmyFaction.Crusaders && controller.SelectedArmy != army)
            {
                UiSound.Play(Kingmaker.UI.UISoundType.ArmyManagementArmySelectPlay);
                controller.SetSelectedArmy(army);
                return;
            }
            Tts.Speak(ArmyLabel(army, GlobalMapCursor.Position));
        }

        /// <summary>The game's right-click on an army pawn (<c>GlobalMapArmyOvertipItemPCView.OnGlobalMapArmyPawnClick</c>):
        /// open its overtip tooltip as the persistent Info window — name, type, squads, general, and for an
        /// enemy on a location its resources / loot / experience / siege state. The window itself is the
        /// game's (<c>InfoWindowVM</c>), read by <see cref="WrathAccess.Screens.InfoWindowScreen"/>.</summary>
        public static void ArmyInfo(GlobalMapArmyState army)
        {
            if (army == null) return;
            Kingmaker.UI.MVVM._VM.Tooltip.Utils.TooltipHelper.ShowInfo(
                new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateArmyOvertip(army));
        }

        /// <summary>Name + side (ally/enemy) + bearing + miles from <paramref name="from"/> (the world-map
        /// cursor for the army cycles) — for the army cycles and the review cursor's interact.</summary>
        public static string ArmyLabel(GlobalMapArmyState army, Vector3 from)
        {
            var parts = new List<string>
            {
                ArmyName(army),
                Loc.T(army.Data.Faction == ArmyFaction.Crusaders ? "worldmap.army_ally" : "worldmap.army_enemy"),
            };
            var pos = ArmyPosition(army);
            if (pos.HasValue)
            {
                if (!Geo.IsHere(from, pos.Value))
                {
                    parts.Add(Geo.Bearing(from, pos.Value));
                    parts.Add(Geo.MilesStr(Geo.Distance(from, pos.Value)));
                }
            }
            return string.Join(", ", parts);
        }
    }
}
