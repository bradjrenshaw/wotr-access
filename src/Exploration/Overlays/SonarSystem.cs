using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A staggered sonar sweep. Rather than looping every visible sonifiable thing at once — which
    /// phantom-centres two same-type sounds at left+right into a single averaged source — it pings them one
    /// at a time, ordered left→right, each positioned by distance (volume) and bearing (pan), then rests and
    /// repeats. The per-ping gap shrinks as the crowd grows: <c>gap = clamp(K/count, gap_min, gap_max)</c>,
    /// so a few things feel spacious and many compress toward the audible floor — the sweep lengthens with
    /// count but nothing is ever dropped (the scanner remains the tool for exact enumeration). Visible-but-
    /// distant things stay in the sweep (quiet, by distance). Self-gates on the overlay being active.
    /// </summary>
    internal sealed class SonarSystem : AudioSystem
    {
        public override string Name => "Sonar";
        public override string Key => "sonar";

        private readonly SfxPlayer _sfx = new SfxPlayer();
        private readonly List<ScanItem> _sweep = new List<ScanItem>(); // ordered snapshot for the current sweep
        private int _index;   // next thing in _sweep to ping
        private float _timer; // seconds until the next ping / until the end-of-sweep rest elapses

        private const float SpreadSec = 0.75f;   // K: the per-ping gap at one thing (then clamped by gap_min/max)
        private const float MinVol = 0.08f;      // floor so far-but-visible things stay audible
        private const float PanWidthFeet = 10f;  // pan crossover (lateral close, bearing far)

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.IntSetting("ref_distance", "Reference distance (feet)", 10, 1, 60, 1, "overlay.sonar.ref_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("max_distance", "Maximum distance (feet)", 40, 10, 120, 5, "overlay.sonar.max_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_min", "Minimum ping gap (ms)", 100, 30, 400, 10, "overlay.sonar.gap_min"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_max", "Maximum ping gap (ms)", 200, 50, 600, 10, "overlay.sonar.gap_max"));
            cat.Add(new WrathAccess.Settings.IntSetting("rest", "Rest between sweeps (ms)", 400, 0, 1500, 50, "overlay.sonar.rest"));
        }

        public override void OnExit(Overlay overlay) => ResetSweep();

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !Enabled) { ResetSweep(); return; }

            _timer -= dt;
            if (_timer > 0f) return;

            // Whole snapshot fired (or none yet) → start a fresh sweep.
            if (_index >= _sweep.Count)
            {
                Snapshot(overlay);
                _index = 0;
                if (_sweep.Count == 0) { _timer = RestSec; return; } // nothing visible — idle, recheck after a rest
            }

            FirePing(_sweep[_index++], overlay); // positioned live, in case the cursor moved during the sweep
            _timer = _index >= _sweep.Count ? RestSec : GapSec(_sweep.Count);
        }

        private void ResetSweep() { _sweep.Clear(); _index = 0; _timer = 0f; }

        // Visible sonifiable things within the sense radius of the cursor, ordered left→right by lateral
        // offset so the pan glides across the sweep (two same-type things read as "left … right", not a
        // centred average). The radius cap stops a far-but-revealed thing from flooring at min volume and
        // sounding deceptively close — out past it, it simply drops from the sweep.
        private void Snapshot(Overlay overlay)
        {
            var c = overlay.Cursor.Position;
            float maxDist = Int("max_distance", 40) * Geo.MetresPerFoot;
            _sweep.Clear();
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible || it.SonarSound == null) continue;
                float dx = it.Position.x - c.x, dz = it.Position.z - c.z;
                float edge = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - it.Footprint);
                if (edge > maxDist) continue;
                _sweep.Add(it);
            }
            _sweep.Sort((a, b) => (a.Position.x - c.x).CompareTo(b.Position.x - c.x));
        }

        private void FirePing(ScanItem item, Overlay overlay)
        {
            if (!item.IsVisible) return; // went away since the snapshot
            var snd = item.SonarSound;
            if (snd == null) return;

            var c = overlay.Cursor.Position;
            var p = item.Position;
            float dx = p.x - c.x, dz = p.z - c.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float fp = item.Footprint;
            float refDist = Int("ref_distance", 10) * Geo.MetresPerFoot;
            float panWidth = PanWidthFeet * Geo.MetresPerFoot;

            // Volume: within the footprint → max; outside → from the nearest surface (closest-point).
            float edge = Mathf.Max(0f, dist - fp);
            float vol = Mathf.Clamp(refDist / (refDist + edge), MinVol, 1f) * EffectiveVolume;
            // Pan: lateral offset up close, bearing farther out; centred when within the footprint.
            float pan = dist > fp ? Mathf.Clamp(dx / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;

            _sfx.Play(Path.Combine(OverlayAudio.Dir, "interactables", snd + ".wav"), vol, pan);
        }

        // gap = clamp(K/count, gap_min, gap_max): spacious for a few, compressing toward the floor as the
        // crowd grows, so the whole sweep lengthens with count but pings stay individually audible.
        private float GapSec(int count)
            => Mathf.Clamp(SpreadSec / Mathf.Max(1, count), Int("gap_min", 100) / 1000f, Int("gap_max", 200) / 1000f);

        private float RestSec => Int("rest", 400) / 1000f;
    }
}
