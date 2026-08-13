using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The map screen's review cursor — single-key cycles over the map's content, nearest-first from
    /// the map cursor (the same keys as in-area review, routed here by the LocalMap input category):
    /// comma = party, period = enemies, N = neutrals, B = all map markers (the game's curated points of
    /// interest — quest / exits / loot / people / other), M = exit markers only; Shift reverses. Units
    /// come from the shared <see cref="WorldModel"/> (what the in-area scanner sees); markers come
    /// straight from the game's <see cref="LocalMapModel"/> — the map's own data layer — wrapped as
    /// <see cref="ProxyMarker"/> (markers are ANNOTATIONS: reviewable and jumpable-to, never
    /// interactable — the reason they moved here off the in-area scanner). The selection is what
    /// <see cref="LocalMapCursor.JumpToReview"/> snaps to.
    /// </summary>
    internal static class LocalMapReview
    {
        internal enum Group { Party, Enemies, Neutrals, Markers, Exits }

        // The marker cache (rebuilt on open and on each cycle press — reveals change it) doubles as the
        // movement cursor's per-frame hover set, so it must stay a stable indexed list.
        private static readonly List<ScanItem> _markers = new List<ScanItem>();
        private static readonly List<ScanItem> _cycle = new List<ScanItem>();
        private static Group _group;
        private static int _index = -1;
        private static ScanItem _selected;

        public static IReadOnlyList<ScanItem> CachedMarkers => _markers;

        public static ScanItem Selected => _selected;

        public static void Reset()
        {
            _cycle.Clear(); _selected = null; _index = -1;
            RebuildMarkers();
        }

        /// <summary>Fresh ProxyMarkers for every live, revealed, in-area map marker (party dots and the
        /// transient destination flag stay out, as everywhere).</summary>
        public static void RebuildMarkers()
        {
            _markers.Clear();
            foreach (var m in LocalMapModel.Markers)
            {
                if (m == null || ProxyMarker.IsNoise(m)) continue;
                try
                {
                    if (!m.IsVisible() || !LocalMapModel.IsInCurrentArea(m.GetPosition())) continue;
                }
                catch { continue; }
                _markers.Add(new ProxyMarker(m));
            }
        }

        public static void Cycle(Group g, int dir)
        {
            RebuildMarkers();
            var from = LocalMapCursor.Position;

            _cycle.Clear();
            switch (g)
            {
                case Group.Party: AddUnits(ScanTaxonomy.UnitsParty); break;
                case Group.Enemies: AddUnits(ScanTaxonomy.UnitsEnemies); break;
                case Group.Neutrals: AddUnits(ScanTaxonomy.UnitsNeutrals); break;
                case Group.Markers: _cycle.AddRange(_markers); break;
                case Group.Exits:
                    foreach (var m in _markers) if (m.Primary == "poi.exits") _cycle.Add(m);
                    break;
            }

            if (_cycle.Count == 0)
            {
                _selected = null; _index = -1; _group = g;
                Tts.Speak(Loc.T("localmap.none", new { group = GroupWord(g) }), interrupt: true);
                return;
            }

            _cycle.Sort((a, b) => a.DistanceTo(from).CompareTo(b.DistanceTo(from)));
            if (g != _group || _index < 0) _index = dir > 0 ? 0 : _cycle.Count - 1;
            else _index = ((_index + dir) % _cycle.Count + _cycle.Count) % _cycle.Count;
            _group = g;
            _selected = _cycle[_index];
            // Positional ping from the map cursor (same sonar voice as in-area review) + the readout.
            var overlay = Overlays.OverlayManager.ActiveOverlay ?? Overlays.OverlaySettingsRegistry.DefaultOverlay;
            overlay?.Get<Overlays.SonarSystem>()?.PlayPathCue(_selected, from);
            Tts.Speak(_selected.Describe(from), interrupt: true);
        }

        // Units as the shared world model knows them (the same proxies the in-area scanner reviews).
        // The MAP reviews what's been DISCOVERED, not what's currently in sight: gate on the game's
        // persistent per-unit reveal flag (the same one its own unit map markers key on —
        // AddLocalMapMarker.IsVisible), with NO distance/LOS restriction, unlike the in-area cycles.
        // A revealed-but-fogged unit reports its last-known (current) position, as the map would.
        private static void AddUnits(string primary)
        {
            foreach (var it in WorldModel.Items)
            {
                if (it.Primary != primary) continue;
                var u = it.TargetUnit;
                if (u != null ? u.IsRevealed : it.IsVisible) _cycle.Add(it);
            }
        }

        private static string GroupWord(Group g)
        {
            switch (g)
            {
                case Group.Party: return Loc.T("localmap.group.party");
                case Group.Enemies: return Loc.T("localmap.group.enemies");
                case Group.Neutrals: return Loc.T("localmap.group.neutrals");
                case Group.Exits: return Loc.T("localmap.group.exits");
                default: return Loc.T("localmap.group.markers");
            }
        }
    }
}
