using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Globalmap.Blueprints; // GlobalMapPointType
using Kingmaker.Globalmap.View;
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays; // OverlayAudio
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map sonar — a staggered sweep around the movement cursor, mirroring the in-area
    /// <see cref="Overlays.SonarSystem"/> (ping nearby points one at a time, left→right, each positioned by
    /// distance (volume) and lateral offset (pan); gap shrinks with the crowd; rest between sweeps). Isolated
    /// from the overlay, but reuses the same shared sonar volume (<c>audio.volumes.sonar</c>) and sound files
    /// (locations ping as "transition", junctions as "unknown"). Distances are in world units (the map's
    /// scale); a real settings pass (and per-category sounds) comes later. Ticked from Main, no-op off the map.
    /// </summary>
    internal static class GlobalMapSonar
    {
        private const float RefDist = 12f;   // units: within this, near-full volume
        private const float MaxDist = 45f;   // units: beyond, dropped from the sweep
        private const float PanWidth = 10f;  // units: lateral-vs-bearing pan crossover
        private const float MinVol = 0.08f;  // floor so far-but-near points stay audible
        private const float SpreadSec = 0.75f;
        private const float GapMin = 0.10f, GapMax = 0.20f, Rest = 0.40f;

        private static readonly List<GlobalMapPointView> _sweep = new List<GlobalMapPointView>();
        private static int _index;
        private static float _timer;

        public static void Reset() { _sweep.Clear(); _index = 0; _timer = 0f; }

        public static void Tick(float dt)
        {
            if (!GlobalMapModel.Active || !FocusMode.Active || SonarVolume() <= 0f) { Reset(); return; }

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
        private static void Snapshot()
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
            _sweep.Sort((a, b) => (a.transform.position.x - c.x).CompareTo(b.transform.position.x - c.x));
        }

        // The world-map taxonomy node a point sounds as (its sound is set in the Scanner tab).
        private static string NodeKey(GlobalMapPointView p)
            => p.Blueprint.Type == GlobalMapPointType.Location ? GlobalMapTaxonomy.Locations.Key : GlobalMapTaxonomy.Junctions.Key;

        private static void FirePing(GlobalMapPointView p)
        {
            var c = GlobalMapCursor.Position;
            var pos = p.transform.position;
            float dx = pos.x - c.x, dz = pos.z - c.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            string stem = ScanSounds.Resolve(NodeKey(p)); // the user's per-type pick (Scanner tab)
            if (stem == null) return; // went Silent since the snapshot
            float vol = Mathf.Clamp(RefDist / (RefDist + dist), MinVol, 1f) * SonarVolume();
            float pan = Mathf.Clamp(dx / Mathf.Max(dist, PanWidth), -1f, 1f);
            AudioEngines.NAudio.PlayOneShot(stem, Path.Combine(OverlayAudio.Dir, "interactables", stem + ".wav"), pos, vol, pan);
        }

        private static float SonarVolume()
            => (ModSettings.GetSetting<IntSetting>("audio.volumes.sonar")?.Get() ?? 100) / 100f * OverlayAudio.Master;
    }
}
