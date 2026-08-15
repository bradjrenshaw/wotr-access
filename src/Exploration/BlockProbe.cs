using Kingmaker.View; // ObstacleAnalyzer
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Identifies what's physically blocking movement at a navmesh stop. Candidates come from a
    /// sphere overlap just past the edge (colliders) and the A* clipper registry (colliderless
    /// navmesh cuts — much furniture). Selection is DIRECTIONAL: candidates roughly in front of the
    /// push always win; an off-axis candidate (a wall corner clipped at an angle) is accepted only
    /// when the navmesh edge is NARROW there — if laterally offset traces stop at the same depth,
    /// the front is wide, meaning something squarely ahead stopped us, and a side wall at right
    /// angles must not claim the blame (live case: "wall left + obstacle ahead" read as wall).
    /// Classification is by collider height: floor-to-ceiling (≥2m from floor) = WALL, shorter or a
    /// navmesh cut = OBSTACLE. Non-allocating: the wall tones call this 4× per frame.
    /// </summary>
    internal static class BlockProbe
    {
        public enum Kind { None, Wall, Obstacle }

        public struct Result
        {
            public Kind Kind;
            public GameObject Object;  // may be null even when classified (anonymous geometry is still typed)
            public float Distance;     // approx metres from the edge — candidate ranking for the namer
        }

        private const float ProbeAhead = 0.45f;   // sphere centre this far past the edge
        private const float ProbeRadius = 0.7f;
        private const float ProbeHeight = 0.9f;   // centre height above the edge: catches crates AND walls
        private const float WallHeight = 2f;      // collider taller than this (from the floor) = a wall
        private const float NearLateral = 0.45f;  // "roughly in front" band around the push line
        private const float FarLateral = 0.9f;    // corner-clip band — needs the narrow-front check
        private const float CutAhead = 1.8f;      // cut CENTRE at most this far past the edge (half a table)
        private const float CutLateral = 0.9f;

        private const float DeepReach = 3.5f;     // last-resort ray: cave wall collider faces sit
                                                  // metres past the eroded navmesh edge (live: 1.3-3m
                                                  // in Prologue_Caves_1) — far beyond the sphere

        private static readonly Collider[] Buf = new Collider[24];
        private static readonly RaycastHit[] RayBuf = new RaycastHit[16];

        /// <summary>Asset name says wall: "wall"/"fence" anywhere in the name counts as a WALL
        /// regardless of collider height (a 1.2m fence is a wall for navigation, not furniture).</summary>
        public static bool WallName(string n) =>
            n != null
            && (n.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("fence", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>What stopped a push from <paramref name="origin"/> that hit the navmesh edge at
        /// <paramref name="edge"/> going <paramref name="dir"/> (XZ-normalized). For a dead-stopped
        /// cursor, origin == edge.</summary>
        public static Result Find(Vector3 origin, Vector3 edge, Vector3 dir)
        {
            // ---- colliderless navmesh cuts, roughly ahead ----
            GameObject cutGo = null;
            float cutAlong = float.MaxValue;
            var cuts = Pathfinding.NavmeshClipper.allEnabled;
            if (cuts != null)
                for (int i = 0; i < cuts.Count; i++)
                {
                    var cut = cuts[i];
                    if (cut == null) continue;
                    var p = cut.transform.position;
                    float ox = p.x - edge.x, oz = p.z - edge.z;
                    float along = ox * dir.x + oz * dir.z;
                    if (along < -0.2f || along > CutAhead || along >= cutAlong) continue;
                    float lx = ox - along * dir.x, lz = oz - along * dir.z;
                    if (lx * lx + lz * lz > CutLateral * CutLateral) continue;
                    cutGo = cut.gameObject;
                    cutAlong = along;
                }
            // Centre-to-surface allowance: a cut's transform sits mid-object, its surface ~0.6m nearer.
            float cutScore = cutGo != null ? Mathf.Max(0.1f, cutAlong - 0.6f) : float.MaxValue;

            // ---- colliders just past the edge ----
            var anchor = edge + Vector3.up * ProbeHeight;
            int n = Physics.OverlapSphereNonAlloc(anchor + dir * ProbeAhead, ProbeRadius, Buf, -1,
                QueryTriggerInteraction.Ignore);
            Collider bestNear = null, bestFar = null;
            float nearScore = float.MaxValue, farScore = float.MaxValue, nearDist = 0f, farDist = 0f;
            for (int i = 0; i < n; i++)
            {
                var col = Buf[i];
                var b = col.bounds;
                if (b.size.y < 0.3f) continue;                  // floor sheets/carpets — surfaces, not blockers
                if (col.attachedRigidbody != null) continue;    // dynamic debris
                if (col.GetComponentInParent<Kingmaker.View.UnitEntityView>() != null) continue; // units: scanner's job
                // ClosestPointOnBounds (not ClosestPoint): valid for ALL collider types incl. concave
                // meshes, and AABB precision is plenty for ranking.
                var cp = col.ClosestPointOnBounds(anchor);
                float ax = cp.x - edge.x, az = cp.z - edge.z;
                float ahead = ax * dir.x + az * dir.z;
                if (ahead < -0.1f) continue;                    // behind the push — a corridor's OTHER wall
                float lx = ax - ahead * dir.x, lz = az - ahead * dir.z;
                float lat = Mathf.Sqrt(lx * lx + lz * lz);
                if (lat > FarLateral) continue;
                float score = Mathf.Max(0f, ahead) + lat;       // in-front beats off-axis at equal reach
                float dist = Mathf.Max(0.05f, Mathf.Sqrt(ax * ax + az * az));
                if (lat <= NearLateral && score < nearScore) { bestNear = col; nearScore = score; nearDist = dist; }
                if (score < farScore) { bestFar = col; farScore = score; farDist = dist; }
            }

            // ---- selection ----
            if (bestNear != null && nearScore <= cutScore)
                return Classified(bestNear, edge, nearDist);
            if (cutGo != null)
                return new Result { Kind = Kind.Obstacle, Object = cutGo, Distance = Mathf.Max(0.1f, cutAlong) };
            if (bestFar != null && FrontIsNarrow(origin, edge, dir))
                return Classified(bestFar, edge, farDist);

            // Last resort — a straight deep ray past the edge: cave/terrain walls have colliders,
            // but their faces sit metres beyond the eroded navmesh edge, out of the sphere's reach.
            // Inherently directional (along the push line), so no lateral/narrow-front guards.
            Collider deep = null;
            float deepDist = float.MaxValue;
            foreach (float h in DeepHeights)
            {
                int m = Physics.RaycastNonAlloc(edge + Vector3.up * h, dir, RayBuf, DeepReach, -1,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++)
                {
                    var col = RayBuf[i].collider;
                    if (col == null || RayBuf[i].distance >= deepDist) continue;
                    if (col.bounds.size.y < 0.3f) continue;
                    if (col.attachedRigidbody != null) continue;
                    if (col.GetComponentInParent<Kingmaker.View.UnitEntityView>() != null) continue;
                    deep = col;
                    deepDist = RayBuf[i].distance;
                }
            }
            if (deep != null) return Classified(deep, edge, deepDist);
            return default;
        }

        private static readonly float[] DeepHeights = { 0.5f, 1.6f };

        private static Result Classified(Collider col, Vector3 edge, float dist)
        {
            var b = col.bounds;
            var kind = (WallName(col.gameObject.name) || (b.size.y >= WallHeight && b.min.y <= edge.y + 0.6f))
                ? Kind.Wall : Kind.Obstacle;
            return new Result { Kind = kind, Object = col.gameObject, Distance = dist };
        }

        // Is the navmesh edge NARROW across the push line? Trace the same direction from starts
        // offset half a metre to each side: if BOTH also stop at (or before) the centre's depth,
        // the blocking front is wide — something squarely ahead stopped us and an off-axis wall
        // corner must not be blamed. One side running longer = a corner; blame the side candidate.
        private static bool FrontIsNarrow(Vector3 origin, Vector3 edge, Vector3 dir)
        {
            float ex = edge.x - origin.x, ez = edge.z - origin.z;
            float depth = Mathf.Sqrt(ex * ex + ez * ez);
            var perp = new Vector3(dir.z, 0f, -dir.x);
            for (int s = -1; s <= 1; s += 2)
            {
                var start = origin + perp * (0.5f * s);
                var traced = ObstacleAnalyzer.TraceAlongNavmesh(start, start + dir * (depth + 0.7f));
                float tx = traced.x - start.x, tz = traced.z - start.z;
                if (tx * tx + tz * tz > (depth + 0.35f) * (depth + 0.35f))
                    return true; // this side runs deeper — the front doesn't span the push line
            }
            return false;
        }
    }
}
