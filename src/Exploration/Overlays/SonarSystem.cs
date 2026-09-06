using System;
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

        private readonly List<ScanItem> _sweep = new List<ScanItem>(); // ordered snapshot for the current sweep
        private int _index;   // next thing in _sweep to ping
        private float _timer; // seconds until the next ping / until the end-of-sweep rest elapses

        private const float SpreadSec = 0.75f;   // K: the per-ping gap at one thing (then clamped by gap_min/max)
        private const float MinVol = 0.08f;      // floor so far-but-visible things stay audible
        private const float PanWidthFeet = 10f;  // pan crossover (lateral close, bearing far)

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            // The review-cursor sounds, one per path-probe outcome (was a single stem dropdown):
            // each outcome's sound is user-pickable from the root wavs, Silent mutes that outcome.
            var review = new WrathAccess.Settings.CategorySetting("review_sounds", "Review cursor sounds",
                includeInPath: true, localizationKey: "overlay.sonar.review_sounds");
            review.Add(new WrathAccess.Settings.ChoiceSetting("straight", "Straight line",
                ReviewSoundChoices(), StemDefault("review_straight"), "overlay.sonar.review.straight"));
            review.Add(new WrathAccess.Settings.ChoiceSetting("path", "Path around",
                ReviewSoundChoices(), StemDefault("review_path"), "overlay.sonar.review.path"));
            review.Add(new WrathAccess.Settings.ChoiceSetting("unreachable", "Unreachable",
                ReviewSoundChoices(), StemDefault("review_unreachable"), "overlay.sonar.review.unreachable"));
            review.Add(new WrathAccess.Settings.ChoiceSetting("los", "Blocked sight",
                ReviewSoundChoices(), StemDefault("review_los"), "overlay.sonar.review.los"));
            cat.Add(review);
            // Sonar mode (user + gdaccess collaboration): "sweep" = the serialized left-to-right
            // pass below; "pulse" = every candidate repeats its own tone, period shrinking with
            // distance and left/right offsetting the phase (see SonarField). Both share the same
            // candidate filter, sounds, volumes and pan.
            cat.Add(new WrathAccess.Settings.ChoiceSetting("sonar_mode", "Sonar mode",
                new System.Collections.Generic.List<WrathAccess.Settings.Choice>
                {
                    new WrathAccess.Settings.Choice("sweep", "Sweep", "choice.sweep"),
                    new WrathAccess.Settings.Choice("pulse", "Pulse", "choice.pulse"),
                }, "sweep", "overlay.sonar.mode"));
            cat.Add(new WrathAccess.Settings.IntSetting("period_near", "Pulse period up close (ms)", 300, 60, 500, 10, "overlay.sonar.period_near"));
            cat.Add(new WrathAccess.Settings.IntSetting("period_far", "Pulse period at max distance (ms)", 600, 300, 2000, 50, "overlay.sonar.period_far"));
            cat.Add(new WrathAccess.Settings.IntSetting("ref_distance", "Reference distance (feet)", 10, 1, 60, 1, "overlay.sonar.ref_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("max_distance", "Maximum distance (feet)", 40, 10, 120, 5, "overlay.sonar.max_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_min", "Minimum ping gap (ms)", 100, 30, 400, 10, "overlay.sonar.gap_min"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_max", "Maximum ping gap (ms)", 200, 50, 600, 10, "overlay.sonar.gap_max"));
            cat.Add(new WrathAccess.Settings.IntSetting("rest", "Rest between sweeps (ms)", 400, 0, 1500, 50, "overlay.sonar.rest"));
        }

        // The review-sound dropdown: the wavs at the audio root (assets/audio/*.wav — where the
        // review_*.wav cues live) plus Silent. User-dropped files appear under their raw stem.
        private static System.Collections.Generic.List<WrathAccess.Settings.Choice> ReviewSoundChoices()
        {
            var choices = new System.Collections.Generic.List<WrathAccess.Settings.Choice>
            {
                new WrathAccess.Settings.Choice("silent", "Silent", "choice.silent"),
            };
            try
            {
                var stems = new System.Collections.Generic.List<string>();
                foreach (var f in Directory.GetFiles(OverlayAudio.Dir, "*.wav"))
                    stems.Add(Path.GetFileNameWithoutExtension(f));
                stems.Sort(System.StringComparer.OrdinalIgnoreCase);
                foreach (var s in stems) choices.Add(new WrathAccess.Settings.Choice(s, s, "sound." + s));
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[sonar] couldn't list review sounds: " + e.Message);
            }
            return choices;
        }

        // A per-outcome default: the shipped wav's stem when it exists, else Silent.
        private static string StemDefault(string stem)
        {
            try { if (File.Exists(Path.Combine(OverlayAudio.Dir, stem + ".wav"))) return stem; }
            catch { }
            return "silent";
        }

        /// <summary>The review ping (cycle/scanner landings and the explicit Semicolon ping): probe
        /// sight + route from the reference to the reviewed thing and play the user's sound for the
        /// outcome — blocked sight / sighted-but-unreachable / route-around / straight line —
        /// positioned at the thing, same distance/pan model as the sweep, relative to the given
        /// reference (the movement cursor; anchored there so it does NOT chase it). NOT gated on
        /// Enabled: it's selection feedback, not part of the sweep; pick Silent per outcome to mute.</summary>
        public void PlayPathCue(ScanItem item, Vector3 from)
        {
            if (item == null) return;
            var info = PathProbe.Probe(from, item.Position);
            string outcome = !info.HasLos ? "los"
                : !info.HasPath ? "unreachable"
                : info.IsStraight ? "straight"
                : "path";
            var stem = Settings?.Get<WrathAccess.Settings.CategorySetting>("review_sounds")
                ?.Get<WrathAccess.Settings.ChoiceSetting>(outcome)?.ValueId
                ?? "review_" + outcome; // no settings registered (shouldn't happen) → shipped default
            if (string.IsNullOrEmpty(stem) || stem == "silent") return;
            var np = item.NearestPoint(from);
            float dx = np.x - from.x, dz = np.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            ListenerFrame.ToEar(ref dx, ref dz); // pan in the facing's frame
            AudioEngines.Current.PlaySpatial(Path.Combine(OverlayAudio.Dir, stem + ".wav"),
                VolumeFor(dist), dx, dz, PanWidthM);
        }

        // The sweep's distance→volume curve (reads the live ref-distance + system volume settings).
        private float VolumeFor(float dist)
        {
            float refDist = Int("ref_distance", 10) * Geo.MetresPerFoot;
            return Mathf.Clamp(refDist / (refDist + dist), MinVol, 1f) * EffectiveVolume;
        }

        private static float PanWidthM => PanWidthFeet * Geo.MetresPerFoot;

        public override void OnExit(Overlay overlay) { ResetSweep(); _field?.Reset(); }

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): the overlay stays engaged, but the sonar shouldn't sweep
            // over a scripted scene. ResetSweep so it starts fresh when control returns.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { ResetSweep(); _field?.Reset(); return; }

            if (ChoiceId("sonar_mode", "sweep") == "pulse") { ResetSweep(); PulseTick(dt, overlay); return; }
            _field?.Reset(); // back on sweep: forget the pulse grid so a mode flip restarts clean

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

        // ---- pulse mode (SonarField): per-entity metronomes, no sweep ----
        private SonarField _field;
        private double _clock;
        private readonly System.Collections.Generic.List<SonarField.Candidate> _candidates
            = new System.Collections.Generic.List<SonarField.Candidate>();

        private void PulseTick(float dt, Overlay overlay)
        {
            _clock += dt;
            if (_field == null) _field = new SonarField();
            _field.PeriodNearSec = Int("period_near", 300) / 1000.0;
            _field.PeriodFarSec = Int("period_far", 600) / 1000.0;
            // Distance endpoints ride the existing feet-based knobs: the volume reference distance
            // is "close" (fastest pulse at/under it), the sweep radius cap is "far" (slowest at it).
            _field.DistNear = Int("ref_distance", 10) * Geo.MetresPerFoot;
            _field.DistFar = Int("max_distance", 40) * Geo.MetresPerFoot;

            // The frame's candidates: the SAME filter as the sweep snapshot (sound configured,
            // radius cap, detectable), plus distance and the left/right phase.
            var c = overlay.Cursor.Position;
            float maxDist = _field.DistFar;
            _candidates.Clear();
            foreach (var it in WorldModel.Items)
            {
                if (ScanSounds.Resolve(it.Primary) == null) continue;
                var np = it.NearestPoint(c);
                float dx = np.x - c.x, dz = np.z - c.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > maxDist * maxDist) continue;
                if (!it.DetectableFrom(c)) continue;
                float dist = Mathf.Sqrt(d2);
                ListenerFrame.ToEar(ref dx, ref dz); // phase in the facing's frame, like the pan
                float pan = Mathf.Clamp(dx / Mathf.Max(dist, PanWidthM), -1f, 1f);
                _candidates.Add(new SonarField.Candidate { Item = it, Dist = dist, Phase = (pan + 1f) * 0.5f });
            }

            var fired = _field.Update(_candidates, _clock);
            for (int i = 0; i < fired.Count; i++)
                FirePing(fired[i], overlay); // live-positioned SpatialSources, same as the sweep
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
                if (ScanSounds.Resolve(it.Primary) == null) continue; // no sound configured for this thing
                var np = it.NearestPoint(c); // distance to the nearest part of the actual shape
                float dx = np.x - c.x, dz = np.z - c.z;
                if (dx * dx + dz * dz > maxDist * maxDist) continue;
                // Known + (a party member sees it now, OR a remembered thing under fog with a clear line of
                // sight from the cursor). Anything currently in sight always pings — in combat it'd be jarring
                // for an enemy your party plainly sees to go silent because a table sits between it and the
                // cursor — while a remembered thing behind a wall isn't pinged straight through it. Shared with
                // the review cycles via ScanItem.DetectableFrom so the two stay consistent. (Sweep is
                // distance-capped, so this only runs on nearby candidates.)
                if (!it.DetectableFrom(c)) continue;
                _sweep.Add(it);
            }
            _sweep.Sort((a, b) => (a.Position.x - c.x).CompareTo(b.Position.x - c.x));
        }

        private void FirePing(ScanItem item, Overlay overlay)
        {
            if (!item.IsVisible) return; // went away since the snapshot
            var snd = ScanSounds.Resolve(item.Primary); // live: the user's per-node pick
            if (snd == null) return;

            // A LIVE source: heard from the moving cursor, positioned at the nearest point on the item's
            // actual shape (recomputed as you move, so a wall reads along its length). SpatialSources re-pans
            // and re-attenuates it every frame until the ping finishes — it no longer freezes at fire time.
            WrathAccess.Audio.SpatialSources.Play(
                Path.Combine(OverlayAudio.Dir, "interactables", snd + ".wav"),
                () => overlay.Cursor.Position,
                c => item.NearestPoint(c),
                VolumeFor,
                PanWidthM);
        }

        // gap = clamp(K/count, gap_min, gap_max): spacious for a few, compressing toward the floor as the
        // crowd grows, so the whole sweep lengthens with count but pings stay individually audible.
        private float GapSec(int count)
            => Mathf.Clamp(SpreadSec / Mathf.Max(1, count), Int("gap_min", 100) / 1000f, Int("gap_max", 200) / 1000f);

        private float RestSec => Int("rest", 400) / 1000f;
    }
}
