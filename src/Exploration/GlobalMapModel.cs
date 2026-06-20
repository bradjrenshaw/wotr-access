using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Read-only facade over the world map (<see cref="GlobalMapView"/>) — the world-map analogue of
    /// <see cref="WorldModel"/>. Exposes the party (traveler) position and the map's points categorised by
    /// type. The map lays its points out on the same XZ plane areas use (Y is flat), so <see cref="Geo"/>'s
    /// bearing/distance carry over directly. Armies, edges, and live diffing arrive in later increments.
    /// </summary>
    internal static class GlobalMapModel
    {
        public static bool Active
        {
            get
            {
                var g = Game.Instance;
                return g != null && g.RootUiContext != null && g.RootUiContext.IsGlobalMap
                    && GlobalMapView.Instance != null;
            }
        }

        /// <summary>The party's pawn position on the map (the selected traveler's view).</summary>
        public static Vector3 TravelerPos
        {
            get
            {
                var t = Game.Instance?.GlobalMapController?.SelectedTraveler;
                return (t != null && t.View != null) ? t.View.Position : Vector3.zero;
            }
        }

        /// <summary>The point the party is currently standing on (null mid-travel).</summary>
        public static BlueprintGlobalMapPoint CurrentLocation
            => Game.Instance?.GlobalMapController?.SelectedTraveler?.Location;

        /// <summary>The view of the current location (for its edges / connected points), or null.</summary>
        public static GlobalMapPointView CurrentLocationView
        {
            get
            {
                var bp = CurrentLocation;
                if (bp == null || GlobalMapView.Instance == null) return null;
                var st = GlobalMapView.Instance.State?.GetPointState(bp);
                return st != null ? st.View : null;
            }
        }

        public static IEnumerable<GlobalMapPointView> Points
            => GlobalMapView.Instance != null ? GlobalMapView.Instance.Points : Enumerable.Empty<GlobalMapPointView>();

        /// <summary>Revealed, enterable places (named locations) — what the player browses and travels to.</summary>
        public static IEnumerable<GlobalMapPointView> Locations
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && p.Blueprint.Type == GlobalMapPointType.Location);

        /// <summary>Revealed road junctions (unnamed waypoints) — the road skeleton, not browse targets.</summary>
        public static IEnumerable<GlobalMapPointView> Junctions
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && p.Blueprint.Type.IsWaypoint());
    }
}
