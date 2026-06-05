using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The soundscape: a continuous looping voice for every VISIBLE sonifiable thing
    /// (<see cref="ScanItem.SonarSound"/>), volume by distance and pan by direction from the cursor, so
    /// you build a live picture and home in by moving the cursor toward a sound. Membership is "visible",
    /// not range-capped, so volume keeps distant-but-revealed things audible. Self-gates on
    /// <see cref="OverlayManager.Active"/> (silence when a menu's up).
    /// </summary>
    internal sealed class SonarSystem : OverlaySystem
    {
        public override string Name => "Sonar";

        private readonly Soundscape _scape = new Soundscape();
        private readonly List<VoiceSpec> _specs = new List<VoiceSpec>();

        private const float RefFeet = 10f;     // distance at which volume is ~half
        private const float MinVol = 0.08f;    // floor so far-but-visible things stay audible
        private const float PanWidthFeet = 10f; // pan crossover (lateral close, bearing far)

        public override void OnExit(Overlay overlay) => _scape.Clear();

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) { _scape.Clear(); return; }

            var c = overlay.Cursor.Position;
            float refDist = RefFeet * Geo.MetresPerFoot;
            float panWidth = PanWidthFeet * Geo.MetresPerFoot;

            _specs.Clear();
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                var snd = it.SonarSound;
                if (snd == null) continue;
                var p = it.Position;
                float dx = p.x - c.x, dz = p.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float fp = it.Footprint;
                // Volume: within the footprint → max; outside → from the nearest surface (closest-point).
                float edge = Mathf.Max(0f, dist - fp);
                float vol = Mathf.Clamp(refDist / (refDist + edge), MinVol, 1f);
                // Pan: lateral offset up close, bearing farther out; centred when within.
                float pan = dist > fp ? Mathf.Clamp(dx / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;
                _specs.Add(new VoiceSpec(it, Path.Combine(OverlayAudio.Dir, "interactables", snd + ".wav"), vol, pan));
            }
            _scape.Update(_specs);
        }
    }
}
