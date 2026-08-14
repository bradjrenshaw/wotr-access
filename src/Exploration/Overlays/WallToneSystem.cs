using Kingmaker.View; // ObstacleAnalyzer
using Pathfinding;    // NNInfo / NavmeshBase / GraphNode
using UnityEngine;
using WrathAccess.Audio; // Audio.Engine / IWallTones

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Directional <b>wall tones</b>: looping sounds whose volumes rise as a navmesh edge nears in
    /// each of four directions — RELATIVE TO THE LISTENER'S FACING (user decision): the "north"
    /// voice is always AHEAD (centred), "south" behind (rear-filtered), "east"/"west" right/left —
    /// so the pans stay fixed in ear space and only the TRACE directions rotate with
    /// <see cref="ListenerFrame"/>. Each frame it traces the navmesh in each direction, and
    /// with the OPT-IN <c>split_obstacles</c> setting, CLASSIFIES the hit via
    /// <see cref="WrathAccess.Exploration.BlockProbe"/> (floor-to-ceiling collider = a WALL, tone
    /// set 1; shorter = an OBSTACLE — furniture, a stall, clutter, tone set 2), falling back to a
    /// paired sight ray for colliderless geometry; off = the classic single bank. Two voice banks
    /// run at once; each direction feeds whichever bank its hit classifies into. Self-gates on
    /// <see cref="OverlayManager.Active"/>; releases its voices on exit.
    /// </summary>
    internal sealed class WallToneSystem : AudioSystem
    {
        public override string Name => "Wall tones";
        public override string Key => "walltones";

        private float Range => Int("range", 15) * Geo.MetresPerFoot;

        // EXPERIMENTAL opt-in: classify hits and play obstacles on tone set 2. Off = the classic
        // single bank (everything sounds as a wall; no per-frame classification cost).
        private bool Split => Bool("split_obstacles", false);

        // How far past the navmesh stop the sight ray may reach and still call the hit a WALL —
        // covers the navmesh's agent-radius erosion plus the wall's thickness (live-measured in the
        // Defender's Heart: true walls put the sight stop ~1.2-1.5m past the navmesh edge).
        private const float WallSlack = 1.75f;

        private static readonly Vector3[] DirVecs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left }; // ahead, behind, right, left

        private IWallTones _walls;      // tone set 1 — real walls (sight-blocked)
        private IWallTones _obstacles;  // tone set 2 — navmesh-only blockage (furniture/clutter)
        private readonly Vector3[] _hits = new Vector3[4];
        private readonly float[] _wallVols = new float[4];
        private readonly float[] _obstVols = new float[4];

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.IntSetting("range", "Range (feet)", 15, 1, 40, 1, "overlay.walltones.range"));
            cat.Add(new WrathAccess.Settings.BoolSetting("split_obstacles", "Play different wall tones for obstacles", false,
                "overlay.walltones.split_obstacles"));
        }

        public override void OnEnter(Overlay overlay) { }

        public override void OnExit(Overlay overlay)
        {
            _walls?.Dispose();
            _obstacles?.Dispose();
            _walls = null; _obstacles = null;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): the overlay stays engaged, but wall tones shouldn't play
            // over a scripted scene. Mute (don't dispose) so they resume seamlessly when control returns.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { Mute(); return; }

            bool split = Split;
            if (_walls == null) _walls = AudioEngines.NAudio.CreateWallTones("1");
            if (split && _obstacles == null) _obstacles = AudioEngines.NAudio.CreateWallTones("2");

            var c = overlay.Cursor.Position;
            float v = EffectiveVolume;

            // The four trace directions in WORLD space this frame: the relative set (ahead/behind/
            // right/left) rotated by the facing. Voice pans are fixed in ear space, so this is the
            // whole rotation story for wall tones.
            float fr = ListenerFrame.Facing * Mathf.Deg2Rad;
            float cf = Mathf.Cos(fr), sf = Mathf.Sin(fr);

            // Nearest node once per frame, reused for the 4 direction Linecasts. A FRESH query (no cross-frame
            // hint): the hint short-circuited the game's y/area node selection, which drifted the trace near
            // walls (triangle edges) and shifted the L/R balance. Fresh-once is bit-identical to the original
            // per-direction TraceAlongNavmesh — same node, same Linecasts — just GetNearest run 1x not 4x.
            NNInfo node = ObstacleAnalyzer.GetNearestNode(c);
            var los = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.LineOfSightGeometry.Instance;
            var eye = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.LineOfSightGeometry.EyeShift;
            for (int i = 0; i < 4; i++)
            {
                var rel = DirVecs[i];
                var dir = new Vector3(rel.x * cf + rel.z * sf, 0f, -rel.x * sf + rel.z * cf);
                _hits[i] = TraceFrom(c, node, c + dir * Range);
                float vol = Curve(c, _hits[i]) * v;

                // Classify the blockage. Primary: the collider probe at the navmesh stop —
                // floor-to-ceiling collider = WALL, shorter = obstacle (height is a physical
                // definition sight-lines through doorway gaps can't fool; live case: an alcove
                // threshold read "obstacle" because the fog ray escaped through the open door).
                // No collider there (colliderless cuts, baked-out geometry) → fall back to the
                // sight ray: blocked just past the stop = wall, clear = something you can see over.
                bool wall = true; // single-bank mode: everything sounds on set 1
                if (vol > 0f && split)
                {
                    wall = false;
                    var probe = WrathAccess.Exploration.BlockProbe.Find(c, _hits[i], dir);
                    if (probe.Kind == WrathAccess.Exploration.BlockProbe.Kind.Wall) wall = true;
                    else if (probe.Kind == WrathAccess.Exploration.BlockProbe.Kind.None)
                    {
                        float hx = _hits[i].x - c.x, hz = _hits[i].z - c.z;
                        float d = Mathf.Sqrt(hx * hx + hz * hz);
                        var sightEnd = c + dir * (d + WallSlack) + eye;
                        wall = los == null || los.HasObstacle(c + eye, sightEnd);
                    }
                }
                _wallVols[i] = wall ? vol : 0f;
                _obstVols[i] = wall ? 0f : vol;
            }
            _walls.Update(_hits, _wallVols);
            _obstacles?.Update(_hits, _obstVols); // null until split_obstacles is first enabled
        }

        // Inactive (menu up / disabled): keep the voices alive but silent, so they resume seamlessly.
        private void Mute()
        {
            for (int i = 0; i < 4; i++) { _wallVols[i] = 0f; _obstVols[i] = 0f; }
            _walls?.Update(_hits, _wallVols);
            _obstacles?.Update(_hits, _obstVols);
        }

        // ObstacleAnalyzer.TraceAlongNavmesh, but with a precomputed nearest node so the GetNearest query
        // isn't repeated per direction. The Linecast (null trace list) is the cheap stack-walking part.
        private static Vector3 TraceFrom(Vector3 start, NNInfo node, Vector3 end)
        {
            float dx = start.x - node.position.x, dz = start.z - node.position.z;
            if (dx * dx + dz * dz > 0.0001f) return node.position; // cursor off-navmesh → clamp (matches the game's trace)
            NavmeshBase.Linecast(node.node.Graph as NavmeshBase, start, end, node.node, out var hit, null);
            return hit.point;
        }

        // 0 (no wall within range) → 1 (right at the wall), curved so it bites close in.
        private float Curve(Vector3 c, Vector3 hit)
        {
            float dx = hit.x - c.x, dz = hit.z - c.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist >= Range) return 0f;
            float t = 1f - dist / Range;
            return t * t;
        }
    }
}
