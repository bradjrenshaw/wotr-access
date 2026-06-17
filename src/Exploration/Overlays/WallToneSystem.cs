using System.IO;
using Kingmaker.View; // ObstacleAnalyzer
using Pathfinding; // NNInfo / NavmeshBase / GraphNode — share one nearest-node query across the 4 traces
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Directional <b>wall tones</b>: four looping sounds whose volumes rise as a wall nears in that
    /// cardinal direction. Each frame it traces along the navmesh in each direction and converts the
    /// distance-to-wall into a volume. On the Wwise path the four loops are real 3D emitters parked
    /// AT the traced wall hit points — each wall hums from where it actually is, in the same spatial
    /// frame as the rest of the game's audio; the NAudio engine (north/south centred, east/west
    /// panned) remains as the classic/fallback path. Self-gates on <see cref="OverlayManager.Active"/>
    /// (mutes when a menu's up); releases its voices on exit.
    /// </summary>
    internal sealed class WallToneSystem : AudioSystem
    {
        public override string Name => "Wall tones";
        public override string Key => "walltones";

        private float Range => Int("range", 10) * Geo.MetresPerFoot;

        // Wwise path: one looping emitter per cardinal direction.
        private static readonly string[] DirNames = { "north", "south", "east", "west" };
        private static readonly Vector3[] DirVecs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        private readonly WwiseAudio.Loop[] _loops = new WwiseAudio.Loop[4];
        private bool _wwise;       // loops are running
        private string _loopSet;   // tone set the running loops were started from

        // Classic path (NAudio flat stereo).
        private WallToneEngine _engine;
        private string _loadedSet; // which set the engine has loaded, so a tone_set change reloads

        private GraphNode _cursorNode; // last frame's nearest navmesh node — reused as the GetNearest hint

        private string ToneSet => ChoiceId("tone_set", "1");
        private string SetDir => System.IO.Path.Combine(OverlayAudio.Dir, "walltones", ToneSet);

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.IntSetting("range", "Range (feet)", 10, 1, 40, 1, "overlay.walltones.range"));
            cat.Add(new WrathAccess.Settings.ChoiceSetting("tone_set", "Tone set",
                new[]
                {
                    new WrathAccess.Settings.Choice("1", "Set 1", "overlay.walltones.tone_set.1"),
                    new WrathAccess.Settings.Choice("2", "Set 2", "overlay.walltones.tone_set.2"),
                }, "1", "overlay.walltones.tone_set"));
        }

        // Playback starts lazily in Tick: the Wwise bank comes up asynchronously during boot, so the
        // path choice can't be made at enter time.
        public override void OnEnter(Overlay overlay) { }

        public override void OnExit(Overlay overlay)
        {
            StopWwise();
            _engine?.Dispose();
            _engine = null;
            _loadedSet = null;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !Enabled) { MuteAll(); return; }

            var c = overlay.Cursor.Position;
            float v = EffectiveVolume;

            // All four direction traces start at the cursor, so the nearest-navmesh-node query is identical
            // for each — compute it ONCE here, hinted from last frame so GetNearest is skipped while the
            // cursor stays in the same triangle. This query was the wall-tone GC churn; the per-direction
            // Linecasts below are the cheap part and still run every frame, so accuracy is unchanged.
            NNInfo node = ObstacleAnalyzer.GetNearestNode(c, _cursorNode);
            _cursorNode = node.node;

            if (WwiseAudio.Active)
            {
                if (!_wwise || _loopSet != ToneSet) StartWwise(c);
                if (_wwise)
                {
                    _engine?.Mute();
                    for (int i = 0; i < 4; i++)
                    {
                        var hit = TraceFrom(c, node, c + DirVecs[i] * Range);
                        WwiseAudio.UpdateLoop(_loops[i], hit, Curve(c, hit) * v);
                    }
                    return;
                }
                // StartWwise failed (stems missing from the bank) → classic below.
            }
            else if (_wwise)
            {
                StopWwise(); // engine setting flipped to classic mid-session
            }

            EnsureLoaded();
            if (_engine == null || !_engine.IsLoaded) return;
            _engine.SetVolumes(
                WallVolume(c, node, Vector3.forward) * v, // +Z north
                WallVolume(c, node, Vector3.back) * v,    // -Z south
                WallVolume(c, node, Vector3.right) * v,   // +X east
                WallVolume(c, node, Vector3.left) * v);   // -X west
        }

        private void StartWwise(Vector3 c)
        {
            StopWwise();
            var set = ToneSet;
            bool ok = true;
            for (int i = 0; i < 4; i++)
            {
                _loops[i] = WwiseAudio.StartLoop("walltones_" + set + "_" + DirNames[i], c);
                if (_loops[i] == null) ok = false;
            }
            if (!ok) { StopWwise(); return; } // all four or nothing — a partial compass misleads
            _wwise = true;
            _loopSet = set;
        }

        private void StopWwise()
        {
            for (int i = 0; i < 4; i++)
            {
                WwiseAudio.StopLoop(_loops[i]);
                _loops[i] = null;
            }
            _wwise = false;
            _loopSet = null;
        }

        private void MuteAll()
        {
            _engine?.Mute();
            if (_wwise)
                for (int i = 0; i < 4; i++)
                    WwiseAudio.SetLoopVolume(_loops[i], 0f);
        }

        // Load the configured tone set on demand; rebuild the engine if the user picked a different one.
        private void EnsureLoaded()
        {
            if (_engine != null && _engine.IsLoaded && _loadedSet == ToneSet) return;
            if (_engine != null && _loadedSet != ToneSet) { _engine.Dispose(); _engine = null; }
            if (_engine == null) _engine = new WallToneEngine();
            if (!_engine.IsLoaded) _engine.Load(SetDir);
            _loadedSet = ToneSet;
        }

        // 0 (no wall within range) → 1 (right at the wall), curved so it bites close in.
        private float WallVolume(Vector3 c, NNInfo node, Vector3 dir)
        {
            var hit = TraceFrom(c, node, c + dir * Range);
            return Curve(c, hit);
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
