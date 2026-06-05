using System.IO;
using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Directional <b>wall tones</b>: four looping sounds (via NAudio) whose volumes rise as a wall nears
    /// in that cardinal direction — north/south centred, east/west panned. Each frame it traces along the
    /// navmesh in each direction and converts the distance-to-wall into a volume. Self-gates on
    /// <see cref="OverlayManager.Active"/> (mutes when a menu's up); loads its tone set on enter, releases
    /// it on exit.
    /// </summary>
    internal sealed class WallToneSystem : OverlaySystem
    {
        public override string Name => "Wall tones";

        private const float RangeFeet = 10f;
        private static float Range => RangeFeet * Geo.MetresPerFoot;
        private int _set = 1; // assets/audio/walltones/<set>/

        private WallToneEngine _engine;

        private string SetDir => Path.Combine(OverlayAudio.Dir, "walltones", _set.ToString());

        public override void OnEnter(Overlay overlay)
        {
            if (_engine == null) _engine = new WallToneEngine();
            if (!_engine.IsLoaded) _engine.Load(SetDir);
        }

        public override void OnExit(Overlay overlay)
        {
            _engine?.Dispose();
            _engine = null;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) { _engine?.Mute(); return; }
            if (_engine == null || !_engine.IsLoaded) return;

            var c = overlay.Cursor.Position;
            _engine.SetVolumes(
                WallVolume(c, Vector3.forward), // +Z north
                WallVolume(c, Vector3.back),    // -Z south
                WallVolume(c, Vector3.right),   // +X east
                WallVolume(c, Vector3.left));   // -X west
        }

        // 0 (no wall within range) → 1 (right at the wall), curved so it bites close in.
        private float WallVolume(Vector3 c, Vector3 dir)
        {
            var hit = ObstacleAnalyzer.TraceAlongNavmesh(c, c + dir * Range);
            float dx = hit.x - c.x, dz = hit.z - c.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist >= Range) return 0f;
            float t = 1f - dist / Range;
            return t * t;
        }
    }
}
