using Kingmaker.EntitySystem; // EntityDataBase
using Owlcat.Runtime.Core.Utils; // GeometryUtils
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Spatial readout helpers for the in-area scene. Distance uses the game's own rules metric
    /// (<see cref="GeometryUtils.MechanicsDistance"/> — horizontal, but counts large vertical gaps at
    /// half, so flying/upper-level things read correctly); bearing is the flat XZ compass (0°=N=+Z,
    /// 90°=E=+X), rendered in the user's chosen direction style (<see cref="Directions"/>). See the
    /// exploration-world-model memory. Composed spatial readouts live in Announce.SpatialPart.
    /// </summary>
    internal static class Geo
    {
        /// <summary>
        /// An entity's LIVE world position — the view transform, which the movement agent writes every
        /// frame. NOT <c>entity.Position</c>: <see cref="UnitEntityData"/> overrides that with its data/
        /// logical position (<c>m_Position</c>), which lags the transform during/after a move (the game's
        /// own UnitMoveController treats <c>View.Transform.position</c> and <c>unit.Position</c> as
        /// distinct). Falls back to <c>entity.Position</c> only when there's no view.
        /// </summary>
        public static Vector3 Live(EntityDataBase e)
        {
            if (e == null) return Vector3.zero;
            var view = e.View;
            return view != null ? view.transform.position : e.Position;
        }

        public static float Distance(Vector3 from, Vector3 to) => GeometryUtils.MechanicsDistance(from, to);

        // World space is metres; the game measures everything the player knows (speed, reach, spell
        // ranges) in feet, at the real ratio 0.3048 m/ft (Kingmaker.Utility.Feet). So convert for any
        // spoken distance — raw metres would read as ~1/3 of the feet the player expects. The game floors
        // to whole feet; we round to the nearest foot to stay close to what's on screen.
        public const float MetresPerFoot = 0.3048f;
        public static float Feet(float metres) => metres / MetresPerFoot;
        public static string FeetStr(float metres) => Loc.T("geo.feet", new { feet = Mathf.RoundToInt(Feet(metres)) });

        // The GLOBAL MAP measures distance in MILES, where 1 world unit == 1 mile (see
        // GlobalMapMovementController.MilesTravelled), so its raw XZ distance is already the mileage — NO
        // metres/feet conversion (unlike the in-area scene above). For world-map readouts only.
        public static string MilesStr(float units) => Loc.T("geo.miles", new { miles = Mathf.RoundToInt(units) });

        /// <summary>True when the two points coincide on the XZ plane (the "here" case).</summary>
        public static bool IsHere(Vector3 from, Vector3 to)
            => Mathf.Abs(to.x - from.x) < 0.05f && Mathf.Abs(to.z - from.z) < 0.05f;

        /// <summary>The direction of <paramref name="to"/> from <paramref name="from"/>, rendered in
        /// the user's chosen direction style (see <see cref="Directions"/>).</summary>
        public static string Bearing(Vector3 from, Vector3 to)
        {
            if (IsHere(from, to)) return Loc.T("geo.here");
            float dx = to.x - from.x, dz = to.z - from.z;
            float deg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = +Z (north), 90 = +X (east)
            return Directions.Word(deg);
        }

        /// <summary>A world yaw (degrees, 0 = north = +Z, 90 = east) as a compass word — for FACINGS
        /// (the listener, the camera), which are absolute (the relative/clock styles don't apply).</summary>
        public static string DirectionWord(float deg) => Directions.CompassWord(deg);

        /// <summary>"above"/"below" only past the game's own 1.5 height threshold; else null.</summary>
        public static string Vertical(Vector3 from, Vector3 to)
        {
            float dy = to.y - from.y;
            if (dy > 1.5f) return Loc.T("geo.above");
            if (dy < -1.5f) return Loc.T("geo.below");
            return null;
        }

        public static string Raw(Vector3 v) => Loc.T("geo.pos", new { x = v.x.ToString("0.0"), y = v.y.ToString("0.0"), z = v.z.ToString("0.0") });
    }
}
