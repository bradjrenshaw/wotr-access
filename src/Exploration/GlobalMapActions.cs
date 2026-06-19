using System.Collections.Generic;
using Kingmaker.Globalmap.View;

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

        /// <summary>Name + compass bearing from the party + a state tag (here / closed).</summary>
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

        /// <summary>Travel to a location, or enter the area when standing on it — mirroring the two common
        /// branches of the game's location panel. Announces the outcome or the reason it can't proceed.</summary>
        public static void Go(GlobalMapPointView pv)
        {
            var view = GlobalMapView.Instance;
            if (view == null || pv == null) return;
            string name = Name(pv);

            if (pv.Blueprint == GlobalMapModel.CurrentLocation)
            {
                if (pv.State.IsClosed) { Tts.Speak(Loc.T("worldmap.is_closed", new { name })); return; }
                view.EnterLocation();
                Tts.Speak(Loc.T("worldmap.entering", new { name }));
                return;
            }

            var path = view.CalculatePlayerPathToLocation(pv.Blueprint);
            if (path == null) { Tts.Speak(Loc.T("worldmap.no_route", new { name })); return; }
            view.GoToLocationRevealed(pv);
            Tts.Speak(Loc.T("worldmap.traveling", new { name }));
        }
    }
}
