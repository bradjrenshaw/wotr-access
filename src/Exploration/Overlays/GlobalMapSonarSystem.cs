using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Globalmap.Blueprints; // GlobalMapPointType
using Kingmaker.Globalmap.View;
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The world-map sonar, now a WorldMap-scoped <see cref="OverlaySystem"/> driven by the engaged overlay
    /// (Ctrl+O) — a staggered sweep around the world-map cursor, mirroring the in-area <see cref="SonarSystem"/>
    /// (ping nearby points one at a time, left→right, each positioned by distance (volume) and lateral offset
    /// (pan); gap shrinks with the crowd; rest between sweeps). Reuses the shared sonar volume
    /// (<c>audio.volumes.sonar</c>) and the world-map scan sounds (Scanner tab). Only ticks on the world map
    /// (scope), under an active overlay, and pauses while a location panel is open.
    /// </summary>
    internal sealed class GlobalMapSonarSystem : OverlaySystem
    {
        public override string Name => "World map sonar";
        public override string Key => "worldmap_sonar";
        public override OverlayScope Scope => OverlayScope.WorldMap;

        private const float RefDist = 12f;   // units: within this, near-full volume
        private const float MaxDist = 45f;   // units: beyond, dropped from the sweep
        private const float PanWidth = 10f;  // units: lateral-vs-bearing pan crossover
        private const float MinVol = 0.08f;  // floor so far-but-near points stay audible
        private const float SpreadSec = 0.75f;
        private const float GapMin = 0.10f, GapMax = 0.20f, Rest = 0.40f;

        private readonly List<GlobalMapPointView> _sweep = new List<GlobalMapPointView>();
        private int _index;
        private float _timer;

        // The WORLD-MAP cursor's movement drives the WhenMoving mode (not the in-area cursor's).
        protected override bool MovingNow(Overlay overlay) => Cursor.WorldMap.MovingRecently;

        private void Reset() { _sweep.Clear(); _index = 0; _timer = 0f; }
        public override void OnExit(Overlay overlay) => Reset();

        public override void Tick(float dt, Overlay overlay)
        {
            // Run only under an engaged overlay; pause while a location panel tab stop is open (so it doesn't
            // sweep while the player reads/acts on it), same as the cursor freeze.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl
                || WrathAccess.Screens.GlobalMapScreen.PanelActive
                || SonarVolume() <= 0f) { Reset(); return; }

            _timer -= dt;
            if (_timer > 0f) return;

            if (_index >= _sweep.Count) // whole snapshot fired (or none yet) → fresh sweep
            {
                Snapshot();
                _index = 0;
                if (_sweep.Count == 0) { _timer = Rest; return; }
            }
            FirePing(_sweep[_index++]); // positioned live, in case the cursor moved during the sweep
            _timer = _index >= _sweep.Count ? Rest : Mathf.Clamp(SpreadSec / Mathf.Max(1, _sweep.Count), GapMin, GapMax);
        }

        // Points within the sense radius whose entity-type sound isn't Silent, ordered left→right so the pan
        // glides across the sweep. Junctions default to Silent (assignable in the Scanner tab's Entities tree,
        // like every other scan type), so by default only locations sweep — no opt-in flag needed.
        private void Snapshot()
        {
            var c = GlobalMapCursor.Position;
            _sweep.Clear();
            foreach (var p in GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions))
            {
                if (ScanSounds.Resolve(NodeKey(p)) == null) continue; // Silent type → out of the sweep
                float dx = p.transform.position.x - c.x, dz = p.transform.position.z - c.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) > MaxDist) continue;
                _sweep.Add(p);
            }
            // Left → right as HEARD: the world map's axes are rotated 180 from map north, so order by
            // the ear-frame x (the same rotation the pans use), not raw world x.
            _sweep.Sort((a, b) => EarX(a.transform.position, c).CompareTo(EarX(b.transform.position, c)));
        }

        private static float EarX(Vector3 p, Vector3 c)
        {
            float dx = p.x - c.x, dz = p.z - c.z;
            ListenerFrame.ToEar(ref dx, ref dz);
            return dx;
        }

        // The world-map taxonomy node a point sounds as (its sound is set in the Scanner tab).
        private static string NodeKey(GlobalMapPointView p)
            => p.Blueprint.Type == GlobalMapPointType.Location ? GlobalMapTaxonomy.Locations.Key : GlobalMapTaxonomy.Junctions.Key;

        private void FirePing(GlobalMapPointView p)
        {
            string stem = ScanSounds.Resolve(NodeKey(p)); // the user's per-type pick (Scanner tab)
            if (stem == null) return; // went Silent since the snapshot
            var pos = p.transform.position; // fixed map point; the cursor is the moving listener
            // A LIVE source: re-panned/re-attenuated each frame as the world-map cursor moves, until it ends.
            SpatialSources.Play(
                Path.Combine(OverlayAudio.Dir, "interactables", stem + ".wav"),
                () => GlobalMapCursor.Position,
                _ => pos,
                dist => Mathf.Clamp(RefDist / (RefDist + dist), MinVol, 1f) * SonarVolume(),
                PanWidth);
        }

        /// <summary>The review ping (the m / n / b / . , cycles, the category browse and the explicit
        /// Semicolon ping): the user's sound for the route outcome — straight (one road from the
        /// traveler), path (several roads), unreachable (no road the traveler can take) — positioned at
        /// the target with the sweep's distance/pan model, relative to the WORLD-MAP cursor (anchored
        /// there). The sound picks are the in-area sonar's review sounds, so one set covers both maps.
        /// NOT gated on Enabled: it's selection feedback, not part of the sweep; pick Silent to mute.</summary>
        public void PlayReviewCue(Vector3 target, string outcome, Overlay overlay)
        {
            var stem = overlay?.Get<SonarSystem>()?.ReviewStem(outcome) ?? "review_" + outcome;
            if (string.IsNullOrEmpty(stem) || stem == "silent") return;
            var from = GlobalMapCursor.Position;
            float dx = target.x - from.x, dz = target.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            // Into the head's frame, like the sweep and the in-area ping: the world map's axes are
            // rotated 180 from the game's north (MapFrame.Offset), and the default facing IS map north,
            // so raw world deltas would pan mirrored.
            ListenerFrame.ToEar(ref dx, ref dz);
            AudioEngines.Current.PlaySpatial(Path.Combine(OverlayAudio.Dir, stem + ".wav"),
                Mathf.Clamp(RefDist / (RefDist + dist), MinVol, 1f) * SonarVolume(), dx, dz, PanWidth);
        }

        private static float SonarVolume()
            => (ModSettings.GetSetting<IntSetting>("audio.volumes.sonar")?.Get() ?? 100) / 100f * OverlayAudio.Master;
    }
}
