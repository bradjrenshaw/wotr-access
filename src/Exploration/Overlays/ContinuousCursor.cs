using System.IO;
using System.Reflection;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A precise, free-moving cursor: hold the arrows to glide the world point continuously (like an
    /// emulated cursor, but over the world rather than the screen), at a fixed ft/sec. Unwalkable terrain
    /// blocks it — each frame it traces from the current point toward the intended one along the navmesh
    /// (ObstacleAnalyzer.TraceAlongNavmesh) and stops at the first wall/ledge, so it can't leave walkable
    /// ground.
    ///
    /// Feedback is <b>wall tones</b>: four looping directional sounds (via NAudio, see
    /// <see cref="WallToneEngine"/>) whose volumes rise as a wall nears in that cardinal direction —
    /// north/south centred, east/west panned right/left. Shares the one <see cref="Cursor"/> with the
    /// scanner and tile view, so move-to-cursor / K still work on the point it leaves.
    /// </summary>
    internal sealed class ContinuousCursor : Overlay
    {
        public override string Name => "Continuous mode";

        private const float SpeedFeet = 15f;  // ft/sec
        private const float RangeFeet = 10f;   // wall-tone audible range
        private static float Speed => SpeedFeet * Geo.MetresPerFoot;   // world m/sec
        private static float Range => RangeFeet * Geo.MetresPerFoot;   // world m
        private int _set = 1;                  // wall-tone set (assets/audio/walltones/<set>/)

        private WallToneEngine _engine;
        private Vector3 _cursor;

        private static UnitEntityData Leader
        {
            get { var p = Game.Instance?.Player; return p != null ? p.MainCharacter.Value : null; }
        }
        private static Vector3 Reference => Geo.Live(Leader);

        private static string ModDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private string SetDir => Path.Combine(ModDir, "assets", "audio", "walltones", _set.ToString());

        public override void OnEnter()
        {
            SyncCursor();
            if (_engine == null) _engine = new WallToneEngine();
            if (!_engine.IsLoaded) _engine.Load(SetDir);
            Speak("Continuous mode" + (_engine.IsLoaded ? "" : ", wall tones unavailable"));
        }

        public override void OnExit()
        {
            _engine?.Dispose();
            _engine = null;
        }

        public override void Recenter()
        {
            _cursor = Reference;
            Cursor.Set(_cursor);
            Speak("Cursor on you. " + Geo.Relative(Reference, _cursor));
        }

        public override void AnnounceCurrent()
        {
            SyncCursor();
            Speak(Geo.Relative(Reference, _cursor));
        }

        public override void Tick(float dt)
        {
            // Selected but not actually exploring (menu up / focus off) → go silent, don't move.
            if (!OverlayManager.Active) { _engine?.Mute(); return; }

            SyncCursor();

            float dx = 0f, dz = 0f;
            if (InputManager.Held("nav.up")) dz += 1f;     // +Z = north
            if (InputManager.Held("nav.down")) dz -= 1f;
            if (InputManager.Held("nav.right")) dx += 1f;  // +X = east
            if (InputManager.Held("nav.left")) dx -= 1f;
            if (dx != 0f || dz != 0f)
            {
                var dir = new Vector3(dx, 0f, dz).normalized;
                var intended = _cursor + dir * (Speed * dt);
                _cursor = ObstacleAnalyzer.TraceAlongNavmesh(_cursor, intended); // stops at walls/ledges
                Cursor.Set(_cursor);
            }

            UpdateTones();
        }

        // Resume from the shared cursor (e.g. after the scanner's Home), else start on the player. Unlike
        // the tile view we don't snap to a grid — this cursor is continuous.
        private void SyncCursor()
        {
            if (Cursor.Has) _cursor = Cursor.Position.Value;
            else { _cursor = Reference; Cursor.Set(_cursor); }
        }

        private void UpdateTones()
        {
            if (_engine == null || !_engine.IsLoaded) return;
            _engine.SetVolumes(
                WallVolume(Vector3.forward),  // +Z north
                WallVolume(Vector3.back),     // -Z south
                WallVolume(Vector3.right),    // +X east
                WallVolume(Vector3.left));    // -X west
        }

        // 0 (no wall within range) → 1 (right at the wall), curved so it bites close in.
        private float WallVolume(Vector3 dir)
        {
            var hit = ObstacleAnalyzer.TraceAlongNavmesh(_cursor, _cursor + dir * Range);
            float ddx = hit.x - _cursor.x, ddz = hit.z - _cursor.z;
            float dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
            if (dist >= Range) return 0f;
            float t = 1f - dist / Range;
            return t * t;
        }

        private static void Speak(string msg) { if (!string.IsNullOrEmpty(msg)) Tts.Speak(msg, interrupt: true); }
    }
}
