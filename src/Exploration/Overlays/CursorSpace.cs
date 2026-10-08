using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Everything that is genuinely different between the in-area cursor and the world-map cursor — and
    /// nothing else. A <see cref="Cursor"/> carries one space; the <see cref="MovementMode"/>s ask it for
    /// geometry instead of assuming a navmesh, feet, or the listener facing, so ONE glide class and ONE
    /// tile-step class serve both contexts (the cursor refactor, step four — 2026-10-08).
    /// </summary>
    internal abstract class CursorSpace
    {
        /// <summary>The point's backing store (the area's shared point; the map's own).</summary>
        public abstract Vector3 Get();
        public abstract void Set(Vector3 p);

        /// <summary>The origin for relative readouts and recenter (player / acting unit; traveler).</summary>
        public abstract Vector3 Reference { get; }

        /// <summary>World units per SETTINGS unit: metres per foot in an area; 1 on the map (units are miles).</summary>
        public abstract float Unit { get; }
        public abstract int DefaultCell { get; }  // settings units
        public abstract int DefaultSpeed { get; } // settings units per second

        /// <summary>Rotate an input vector / a step into world axes (listener facing in-area; map north on the map).</summary>
        public abstract void InputToWorld(ref float dx, ref float dz);
        public abstract void StepToWorld(ref int dx, ref int dz);

        /// <summary>One glide step from <paramref name="from"/> toward <paramref name="intended"/>: the
        /// reachable point, or false when blocked / no progress.</summary>
        public abstract bool Trace(Vector3 from, Vector3 intended, bool slide, out Vector3 traced);

        /// <summary>Where a tile landing at (x, z) sits: on the walkable surface if there is one, else at
        /// <paramref name="keepY"/> (the cursor never falls); the map is flat.</summary>
        public abstract Vector3 Land(float x, float z, float keepY);

        /// <summary>Follow a surface to the level below (-1) or above (+1) at (x, z). Flat spaces: unsupported.</summary>
        public virtual VerticalResult FollowLevel(float x, float z, float y, int dir, out Vector3 at)
        {
            at = default;
            return VerticalResult.Unsupported;
        }

        /// <summary>The context's own movement gate, beyond the overlay being active.</summary>
        public abstract bool CanMove { get; }

        /// <summary>Whether the shared explore.* movement keys belong to this cursor right now: its own
        /// screen is on top. (The local map screen reads them for its own cursor, so neither of these does.)</summary>
        public abstract bool OwnsKeys { get; }
    }

    /// <summary>The in-area space: the shared world point, navmesh-bound, feet, listener-relative input.</summary>
    internal sealed class AreaSpace : CursorSpace
    {
        public override Vector3 Get() => WrathAccess.Exploration.Cursor.Has ? WrathAccess.Exploration.Cursor.Position.Value : Reference;
        public override void Set(Vector3 p) => WrathAccess.Exploration.Cursor.Set(p);
        public override Vector3 Reference => Cursor.PlayerPosition;
        public override float Unit => Geo.MetresPerFoot;
        public override int DefaultCell => 5;
        public override int DefaultSpeed => 15;
        public override void InputToWorld(ref float dx, ref float dz) => ListenerFrame.InputToWorld(ref dx, ref dz); // W = forward of the facing
        public override void StepToWorld(ref int dx, ref int dz) => ListenerFrame.StepToWorld(ref dx, ref dz);     // grid stays world-aligned

        // Movement is the one thing gated on having control — so the cursor can't drift during a cutscene.
        public override bool CanMove => WrathAccess.ControlState.HasControl;
        public override bool OwnsKeys => WrathAccess.Screens.ScreenManager.Current?.Key == "ctx.ingame";

        public override bool Trace(Vector3 cur, Vector3 intended, bool slide, out Vector3 traced)
        {
            // Wall slide: blocked motion slides along the wall's tangent — the same trace the game's
            // direct-control movement uses — funnelling through doorways instead of dead-stopping.
            try
            {
                traced = slide
                    ? ObstacleAnalyzer.TraceAlongNavmeshWithWallSlide(cur, intended)
                    : ObstacleAnalyzer.TraceAlongNavmesh(cur, intended); // stops at walls/ledges
            }
            catch (System.NullReferenceException)
            {
                // No navmesh under the cursor yet (a key held through an area-part swap): the game's
                // trace dereferences a null nearest node. Treat as blocked rather than abort the tick.
                traced = cur;
                return false;
            }
            if ((traced - cur).sqrMagnitude < 1e-6f) return false;
            // Re-project onto the walkable surface: the trace's unobstructed result keeps the INPUT Y
            // (the navmesh linecast never re-snaps height), so gliding up a ramp left the cursor's Y
            // fossilized at wherever it was last planted — path-dependent heights on one slope, and a
            // stale feed to the slope indicator. Seeding the sample with the current Y keeps genuine
            // multi-level geometry honest (nearest tier wins; ascend/descend still switch tiers).
            var s = NavmeshProbe.Sample(traced.x, traced.z, traced.y);
            if (s.OnNavmesh) traced.y = s.Point.y;
            return true;
        }

        public override Vector3 Land(float x, float z, float keepY)
        {
            var s = NavmeshProbe.Sample(x, z, keepY);
            return new Vector3(x, s.OnNavmesh ? s.Point.y : keepY, z); // follow the surface; otherwise keep height (never fall)
        }

        public override VerticalResult FollowLevel(float x, float z, float y, int dir, out Vector3 at)
        {
            bool found = dir < 0
                ? NavmeshProbe.FloorBelow(x, z, y, out var floor)
                : NavmeshProbe.FloorAbove(x, z, y, out floor);
            at = found ? new Vector3(x, floor.y, z) : default;
            return found ? VerticalResult.Moved : VerticalResult.NoSurface;
        }
    }

    /// <summary>The world-map space: its own point (a SEPARATE point from the in-area one, so it never
    /// pollutes it), free-roam over the flat XZ plane, miles (1 world unit == 1 mile on the global map),
    /// map-north input. Falls back to the traveler until placed.</summary>
    internal sealed class GlobalMapSpace : CursorSpace
    {
        private Vector3? _pos;

        /// <summary>Forget the placed point (on entering the map) so the cursor starts on the traveler.</summary>
        public void Reset() { _pos = null; _boundsArea = null; }

        public override Vector3 Get() => _pos ?? GlobalMapModel.TravelerPos;
        public override void Set(Vector3 p) => _pos = Clamp(p);

        // ---- the map's edge ----

        private Bounds _bounds;
        private string _boundsArea; // the area the cached bounds were measured for
        private const float EdgePadding = 2f; // miles past the outermost point / camera limit

        /// <summary>The playable rectangle (XZ, world units): the area's camera bounds grown to hold
        /// every point, plus a little padding — where a sighted player can scroll to and see the painted
        /// map end. The cursor is clamped inside it; the wall tones sound its edge.</summary>
        public Bounds MapBounds
        {
            get
            {
                var area = Kingmaker.Game.Instance?.CurrentlyLoadedArea;
                var key = area != null ? area.name : null;
                if (key != null && key == _boundsArea) return _bounds;
                var part = Kingmaker.Game.Instance?.CurrentlyLoadedAreaPart;
                var b = part != null && part.Bounds != null ? part.Bounds.CameraBounds : new Bounds(Get(), Vector3.one * 200f);
                foreach (var p in GlobalMapModel.Points) if (p != null) b.Encapsulate(p.transform.position);
                b.Expand(new Vector3(EdgePadding * 2f, 0f, EdgePadding * 2f));
                _bounds = b; _boundsArea = key;
                return b;
            }
        }

        private Vector3 Clamp(Vector3 p)
        {
            var b = MapBounds;
            return new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), 0f, Mathf.Clamp(p.z, b.min.z, b.max.z));
        }

        /// <summary>Where a ray from <paramref name="from"/> along <paramref name="dir"/> (unit, XZ) meets
        /// the map's edge — the wall-tone "hit" for that direction.</summary>
        public Vector3 EdgeHit(Vector3 from, Vector3 dir)
        {
            var b = MapBounds;
            float t = float.MaxValue;
            if (dir.x > 1e-6f) t = Mathf.Min(t, (b.max.x - from.x) / dir.x);
            else if (dir.x < -1e-6f) t = Mathf.Min(t, (b.min.x - from.x) / dir.x);
            if (dir.z > 1e-6f) t = Mathf.Min(t, (b.max.z - from.z) / dir.z);
            else if (dir.z < -1e-6f) t = Mathf.Min(t, (b.min.z - from.z) / dir.z);
            if (t == float.MaxValue || t < 0f) t = 0f;
            return from + dir * t;
        }
        public override Vector3 Reference => GlobalMapModel.TravelerPos;
        public override float Unit => 1f;
        public override int DefaultCell => 2;
        public override int DefaultSpeed => 18;
        // Up = the map's north (MapFrame; 0 on the world maps, kept for symmetry with the local map).
        public override void InputToWorld(ref float dx, ref float dz) => MapFrame.InputToWorld(ref dx, ref dz);
        public override void StepToWorld(ref int dx, ref int dz) => MapFrame.StepToWorld(ref dx, ref dz);

        // No movement while a location panel tab stop is open (the player is reading/acting on it) or the
        // map isn't in its plain interactive state (rest / dialog / book event / battle overlays own input).
        public override bool CanMove => !WrathAccess.Screens.GlobalMapScreen.PanelActive && GlobalMapModel.Interactive;
        public override bool OwnsKeys => WrathAccess.Screens.ScreenManager.Current?.Key == "ctx.globalmap";

        public override bool Trace(Vector3 cur, Vector3 intended, bool slide, out Vector3 traced)
        {
            traced = Clamp(intended); // the map's edge is the one wall
            return (traced - cur).sqrMagnitude >= 1e-6f;
        }

        public override Vector3 Land(float x, float z, float keepY) => Clamp(new Vector3(x, 0f, z));
    }
}
