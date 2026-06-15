using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A scan item's spatial extent on the XZ plane. <see cref="Center"/> is the thing's middle — where a
    /// cursor snaps. <see cref="NearestPoint"/> is the closest part of the thing to a reference point,
    /// used for the spoken distance/bearing: standing due south of a wide doorway should read "south", not
    /// "west" to its centre. Shapes: a point (default), a circle (a creature/object footprint), a polyline
    /// (a connected chain), and disjoint segments (a doorway's actual portal edges). More can be added
    /// without touching call sites.
    /// </summary>
    internal abstract class ScanBounds
    {
        /// <summary>The thing's middle (cursor target).</summary>
        public abstract Vector3 Center { get; }

        /// <summary>The closest point of the thing to <paramref name="from"/> (XZ); Y carried from the
        /// bounds so above/below still reads. Inside the bounds → returns the reference (distance 0, "here").</summary>
        public abstract Vector3 NearestPoint(Vector3 from);

        public static ScanBounds Point(Vector3 p) => new PointBounds(p);
        public static ScanBounds Circle(Vector3 c, float radius) => new CircleBounds(c, radius);
        /// <summary>A connected chain of ≥1 points; the closest point lies on its consecutive segments.</summary>
        public static ScanBounds Polyline(Vector3 center, IList<Vector3> points) => new PolylineBounds(center, points);
        /// <summary>DISJOINT segments as flat endpoint pairs [a0,b0,a1,b1,…] — e.g. a doorway's actual
        /// portal edges (full opening extent, not a chord). <paramref name="center"/> is the cursor target.</summary>
        public static ScanBounds Segments(Vector3 center, IList<Vector3> edgePairs) => new SegmentsBounds(center, edgePairs);
        /// <summary>A dense point cloud — e.g. an opening's watershed-boundary cell midpoints; the nearest
        /// point is the closest of them (accurate to the cell spacing). <paramref name="center"/> = cursor target.</summary>
        public static ScanBounds Cloud(Vector3 center, IList<Vector3> points) => new CloudBounds(center, points);

        // Closest point on segment a→b to `from`, on the XZ plane (Y lerped along the segment).
        protected static Vector3 ClosestOnSegment(Vector3 from, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float len2 = abx * abx + abz * abz;
            if (len2 < 1e-6f) return a;
            float t = Mathf.Clamp01(((from.x - a.x) * abx + (from.z - a.z) * abz) / len2);
            return new Vector3(a.x + abx * t, Mathf.Lerp(a.y, b.y, t), a.z + abz * t);
        }

        private sealed class PointBounds : ScanBounds
        {
            private readonly Vector3 _p;
            public PointBounds(Vector3 p) { _p = p; }
            public override Vector3 Center => _p;
            public override Vector3 NearestPoint(Vector3 from) => _p;
        }

        private sealed class CircleBounds : ScanBounds
        {
            private readonly Vector3 _c;
            private readonly float _r;
            public CircleBounds(Vector3 c, float r) { _c = c; _r = Mathf.Max(0f, r); }
            public override Vector3 Center => _c;
            public override Vector3 NearestPoint(Vector3 from)
            {
                float dx = from.x - _c.x, dz = from.z - _c.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= _r || d < 1e-4f) return new Vector3(from.x, _c.y, from.z); // inside the footprint → at it
                float t = _r / d;
                return new Vector3(_c.x + dx * t, _c.y, _c.z + dz * t);
            }
        }

        private sealed class PolylineBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts;
            public PolylineBounds(Vector3 center, IList<Vector3> points)
            {
                _center = center;
                _pts = points != null && points.Count > 0 ? new Vector3[points.Count] : new[] { center };
                for (int i = 0; points != null && i < points.Count; i++) _pts[i] = points[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts.Length == 1) return _pts[0];
                Vector3 best = _pts[0];
                float bestD = float.MaxValue;
                for (int i = 0; i + 1 < _pts.Length; i++)
                {
                    var p = ClosestOnSegment(from, _pts[i], _pts[i + 1]);
                    float dx = from.x - p.x, dy = from.y - p.y, dz = from.z - p.z;
                    float d = dx * dx + dy * dy + dz * dz; // 3D so a vertically-distant chain doesn't win
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
        }

        // The closest of a dense point cloud (the opening's watershed-boundary midpoints). 3D so a
        // boundary stretch up on a ledge doesn't win over the threshold at your level.
        private sealed class CloudBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts;
            public CloudBounds(Vector3 center, IList<Vector3> pts)
            {
                _center = center;
                _pts = pts != null && pts.Count > 0 ? new Vector3[pts.Count] : null;
                for (int i = 0; _pts != null && i < pts.Count; i++) _pts[i] = pts[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts == null) return _center;
                Vector3 best = _pts[0];
                float bestD = float.MaxValue;
                for (int i = 0; i < _pts.Length; i++)
                {
                    float dx = from.x - _pts[i].x, dy = from.y - _pts[i].y, dz = from.z - _pts[i].z;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d < bestD) { bestD = d; best = _pts[i]; }
                }
                return best;
            }
        }

        // The closest point over a set of independent segments (each portal edge), so the full opening
        // extent is covered — not just a chord between two midpoints.
        private sealed class SegmentsBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts; // flat pairs: [a0,b0,a1,b1,…]
            public SegmentsBounds(Vector3 center, IList<Vector3> pts)
            {
                _center = center;
                _pts = pts != null && pts.Count >= 2 ? new Vector3[pts.Count] : null;
                for (int i = 0; _pts != null && i < pts.Count; i++) _pts[i] = pts[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts == null) return _center;
                Vector3 best = _center;
                float bestD = float.MaxValue;
                for (int i = 0; i + 1 < _pts.Length; i += 2)
                {
                    var p = ClosestOnSegment(from, _pts[i], _pts[i + 1]);
                    float dx = from.x - p.x, dy = from.y - p.y, dz = from.z - p.z;
                    float d = dx * dx + dy * dy + dz * dz; // 3D: an opening edge up on a ledge shouldn't win
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
        }
    }
}
