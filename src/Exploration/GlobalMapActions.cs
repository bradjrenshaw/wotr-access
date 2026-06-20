using System.Collections.Generic;
using Kingmaker;
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
            var n = (string)p.Blueprint.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("worldmap.junction") : n;
        }

        /// <summary>Name + compass bearing from the party + a state tag (here / closed). For lists.</summary>
        public static string Label(GlobalMapPointView p)
        {
            var parts = new List<string> { Name(p) };
            if (p.Blueprint == GlobalMapModel.CurrentLocation)
            {
                parts.Add(Loc.T("worldmap.you_are_here"));
            }
            else
            {
                var bearing = Geo.Bearing(GlobalMapModel.TravelerPos, p.transform.position);
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

        /// <summary>Travel to a point, or enter the area when standing on it — mirroring the game's location
        /// panel gating. Announces the outcome or the reason it can't proceed.</summary>
        public static void Go(GlobalMapPointView pv)
        {
            var view = GlobalMapView.Instance;
            if (view == null || pv == null) return;
            string name = Name(pv);
            bool atHere = pv.Blueprint == GlobalMapModel.CurrentLocation;

            // Mirror the game's panel gating (GlobalMapEnterMessageView): CLOSED blocks ENTERING always, and
            // blocks TRAVELLING only when the map sets RestrictTravelingToClosedLocations — otherwise you may
            // approach a closed location, you just can't enter it. RESTRICTED blocks both.
            if (pv.State.IsClosed && (atHere || view.Blueprint.RestrictTravelingToClosedLocations))
            {
                Tts.Speak(ClosedText(pv));
                return;
            }
            var restriction = pv.Blueprint.Restriction;
            if (restriction != null && restriction.IsRestricted()) { Tts.Speak(Loc.T("worldmap.restricted", new { name })); return; }

            if (atHere)
            {
                view.EnterLocation();
                Tts.Speak(Loc.T("worldmap.entering", new { name }));
                return;
            }

            var path = view.CalculatePlayerPathToLocation(pv.Blueprint);
            if (path == null) { Tts.Speak(Loc.T("worldmap.no_route", new { name })); return; }
            view.GoToLocationRevealed(pv);
            Tts.Speak(Loc.T("worldmap.traveling", new { name }));
        }

        // The game's closed-location message — a per-location custom override, else the shared "closed" line.
        private static string ClosedText(GlobalMapPointView pv)
        {
            var bp = pv.Blueprint;
            if (bp.UseCustomClosedText && !bp.CustomClosedText.IsEmpty()) return (string)bp.CustomClosedText;
            return (string)Game.Instance.BlueprintRoot.LocalizedTexts.UserInterfacesText.GlobalMap.LocationIsClosed;
        }
    }
}
