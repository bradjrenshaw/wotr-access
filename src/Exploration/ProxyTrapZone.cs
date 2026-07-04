using Kingmaker.View.MapObjects.SriptZones; // ScriptZone + shapes
using Kingmaker.View.MapObjects.Traps; // TrapObjectData/View
using Owlcat.Runtime.Core.Utils; // PolygonComponent (polygon shapes' world points)
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A trap's TRIGGER AREA — the ground you must not step on, as its own scanner entity ("Trap
    /// zones"), separate from the trap device (the "Traps" item you walk to for the disarm). The
    /// game's trap anatomy: <see cref="TrapObjectView.Settings"/>.ScriptZoneTrigger is a
    /// <see cref="ScriptZone"/> whose <see cref="IScriptZoneShape"/>s are the exact hit-test the game
    /// springs the trap with (<c>shape.Contains(unit.Position)</c>); proximity-style traps have no
    /// zone and trigger by radius around the device (<see cref="TrapObjectData.ProximityRadius"/>).
    /// We read the real shape so distance/bearing (and the sonar cue) report the nearest EDGE of the
    /// danger area: cylinder → circle, box → its rotated rectangle, polygon → its world outline,
    /// anything else → its world bounds as a circle. Shown while the trap is DISCOVERED and ARMED —
    /// reveal rides the trap's perception check like the device item, and disarming/springing it
    /// (TrapActive false) removes the zone.
    /// </summary>
    internal sealed class ProxyTrapZone : ProxyEntity
    {
        private readonly TrapObjectData _trap;
        private readonly ScriptZone _zone; // null for proximity-radius traps

        public ProxyTrapZone(TrapObjectData trap)
            : base(trap) // reveal/fog state rides the TRAP entity (the zone entity has no perception data)
        {
            _trap = trap;
            var view = trap.View as TrapObjectView;
            _zone = view != null && view.Settings != null ? view.Settings.ScriptZoneTrigger : null;
        }

        public override string Name => Loc.T("scan.trap_zone");

        // Discovered (the trap's own perception gate, like the device item) and still ARMED.
        public override bool IsVisible
            => _trap.IsInGame && _trap.IsRevealed && _trap.IsPerceptionCheckPassed && _trap.TrapActive;

        // The first live shape of the trigger zone (traps use a single shape; the list is defensive).
        private IScriptZoneShape Shape
        {
            get
            {
                if (_zone == null) return null;
                var shapes = _zone.Shapes;
                for (int i = 0; i < shapes.Count; i++)
                    if (shapes[i] != null) return shapes[i];
                return null;
            }
        }

        // The zone's centre (shape centre → zone transform → the device position for proximity traps).
        public override Vector3 Position
        {
            get
            {
                var s = Shape;
                if (s != null) return s.Center();
                if (_zone != null) return _zone.transform.position;
                return base.Position; // proximity trap: the danger radius is around the device
            }
        }

        // Scalar extent for the circle-based systems (sonar / cues / tile grid).
        public override float Footprint
        {
            get
            {
                var s = Shape;
                if (s is ScriptZoneCylinder cyl) return cyl.Radius;
                if (s != null) { var e = s.GetBounds().extents; return Mathf.Max(e.x, e.z); }
                return _trap.ProximityRadius ?? 0f;
            }
        }

        // The real footprint geometry, so announcements report the nearest EDGE of the danger area.
        public override ScanBounds Bounds
        {
            get
            {
                var s = Shape;
                if (s is ScriptZoneCylinder cyl) return ScanBounds.Circle(s.Center(), cyl.Radius);
                if (s is ScriptZoneBox && TryCorners(out var p0, out var p1, out var p2, out var p3))
                    return ScanBounds.Rect(Position, new[] { p0, p1, p2, p3 });
                if (s is PolygonComponent poly && poly.TransformedPoints != null && poly.TransformedPoints.Length >= 3)
                    return ScanBounds.Rect(Position, poly.TransformedPoints);
                if (s != null) { var b = s.GetBounds(); return ScanBounds.Circle(s.Center(), Mathf.Max(b.extents.x, b.extents.z)); }
                var r = _trap.ProximityRadius ?? 0f;
                return r > 0f ? ScanBounds.Circle(Position, r) : base.Bounds;
            }
        }

        // Per-frame path (sonar / cues / cursor): the same geometry, NON-ALLOCATING via the shared
        // ScanBounds statics (mirrors ProxyAreaEffect).
        public override Vector3 NearestPoint(Vector3 from)
        {
            var s = Shape;
            if (s is ScriptZoneCylinder cyl) return ScanBounds.NearestOnCircleXZ(s.Center(), cyl.Radius, from);
            if (s is ScriptZoneBox && TryCorners(out var p0, out var p1, out var p2, out var p3))
                return ScanBounds.NearestInQuadXZ(from, p0, p1, p2, p3);
            if (s != null) { var b = s.GetBounds(); return ScanBounds.NearestOnCircleXZ(s.Center(), Mathf.Max(b.extents.x, b.extents.z), from); }
            return ScanBounds.NearestOnCircleXZ(Position, _trap.ProximityRadius ?? 0f, from);
        }

        // A box shape's four world-space footprint corners (the rotated rectangle).
        private bool TryCorners(out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            p0 = p1 = p2 = p3 = default;
            if (!(Shape is ScriptZoneBox box)) return false;
            var t = box.transform;
            var b = box.Bounds; // local
            Vector3 c = b.center, e = b.extents;
            p0 = t.TransformPoint(c + new Vector3(-e.x, 0f, -e.z));
            p1 = t.TransformPoint(c + new Vector3( e.x, 0f, -e.z));
            p2 = t.TransformPoint(c + new Vector3( e.x, 0f,  e.z));
            p3 = t.TransformPoint(c + new Vector3(-e.x, 0f,  e.z));
            return true;
        }

        public override System.Collections.Generic.IEnumerable<string> Nodes
        {
            get { yield return ScanTaxonomy.TrapZones; }
        }

        public override string Primary => ScanTaxonomy.TrapZones;
    }
}
