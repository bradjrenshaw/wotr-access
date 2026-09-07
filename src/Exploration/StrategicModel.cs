using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.View.Spawners;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// STRATEGIC hints — the "Strategic" scan category (J / Shift+J): context a sighted player reads
    /// off the scene at a glance that a blind player has no channel for. An <see cref="Enhancements"/>
    /// toggle (default on), since it goes past what is literally visible. Two kinds so far, both
    /// derived live from the loaded scene (never authored):
    /// <list type="bullet">
    /// <item><b>Spawn points</b> — where enemies will appear: the scene's <see cref="UnitSpawnerBase"/>
    /// components whose blueprint faction attacks the player and which have NOT spawned yet, clustered
    /// by proximity (a gate's pack reads as one point). The sighted cue is the gate / breach / ladder
    /// the pack stands behind; we give the point and how many wait, never the composition. A cluster
    /// drops out once all its spawners have fired, so the list shrinks as a defense progresses.</item>
    /// <item><b>Enemy objectives</b> — the invisible marker units an enemy AI runs at instead of the
    /// party (the Defender's Heart arsonists bomb "Tavern" / "Stables" / "Forge" targets): alive
    /// never-fight units whose faction is on the attack list of a hostile faction that has spawners
    /// in this scene. Sighted players see the arsonists sprint for the buildings; this names them.</item>
    /// </list>
    /// Spawner discovery walks every loaded object (<c>Resources.FindObjectsOfTypeAll</c> — allocating),
    /// so it runs on a slow cadence and on area/part change; the per-second state pass over the cached
    /// spawners is cheap. Identity is stable: a cluster object survives rescans while its member set
    /// is unchanged (the review cycle's "continue from current" relies on it), objectives are keyed
    /// by their unit.
    /// </summary>
    internal static class StrategicModel
    {
        private const float ClusterRadius = 6f;   // XZ metres — spawners closer than this are one point
        private const float RescanSec = 30f;      // spawner discovery fallback cadence (allocates); a
                                                  // scene-count change (mechanics scene streaming in) rescans at once
        private const float RefreshSec = 1f;      // pending-count / objective pass over cached data

        internal sealed class SpawnerRec
        {
            public UnitSpawnerBase Spawner;
            public Vector3 Position;
            public BlueprintFaction Faction;
            public bool Pending;                  // not yet spawned (live-refreshed)
            public bool Alive;                    // spawned and its unit is alive in-game
        }

        private static readonly List<SpawnerRec> _spawners = new List<SpawnerRec>();
        private static readonly Dictionary<string, SpawnCluster> _clusters = new Dictionary<string, SpawnCluster>();
        private static readonly List<SpawnCluster> _liveClusters = new List<SpawnCluster>();
        private static readonly Dictionary<string, ObjectiveItem> _objectives = new Dictionary<string, ObjectiveItem>();
        private static readonly List<ObjectiveItem> _liveObjectives = new List<ObjectiveItem>();
        private static readonly HashSet<BlueprintFaction> _hostilePresent = new HashSet<BlueprintFaction>();
        private static readonly List<string> _goneKeys = new List<string>();

        private static string _areaKey;
        private static int _sceneCount = -1;
        private static float _nextRescan, _nextRefresh;

        /// <summary>Spawn-point clusters with at least one spawner still to fire.</summary>
        public static IReadOnlyList<SpawnCluster> Clusters => _liveClusters;
        /// <summary>Enemy-objective markers currently in play.</summary>
        public static IReadOnlyList<ObjectiveItem> Objectives => _liveObjectives;

        /// <summary>Per-tick (from WorldModel.Tick). Cheap unless a cadence fires.</summary>
        public static void Tick()
        {
            if (!Enhancements.StrategicHints) { if (_spawners.Count > 0 || _objectives.Count > 0) Clear(); _areaKey = null; return; }
            var game = Game.Instance;
            var area = game?.CurrentlyLoadedArea;
            if (area == null || game.State == null) { if (_areaKey != null) { Clear(); _areaKey = null; } return; }
            var part = Kingmaker.Blueprints.Area.AreaService.Instance?.CurrentAreaPart;
            string key = area.name + "|" + (part != null ? part.name : "");
            float now = Time.unscaledTime;
            if (key != _areaKey)
            {
                _areaKey = key;
                Clear();
                _nextRescan = 0f; // the mechanics scenes stream in after the part key changes; rescans catch up
            }
            int scenes = UnityEngine.SceneManagement.SceneManager.sceneCount;
            if (now >= _nextRescan || scenes != _sceneCount)
            {
                _sceneCount = scenes; _nextRescan = now + RescanSec; _nextRefresh = 0f;
                Rescan();
            }
            if (now >= _nextRefresh) { _nextRefresh = now + RefreshSec; Refresh(game); }
        }

        private static void Clear()
        {
            _spawners.Clear(); _clusters.Clear(); _liveClusters.Clear();
            _objectives.Clear(); _liveObjectives.Clear(); _hostilePresent.Clear();
        }

        // ---- spawner discovery ----

        private static BlueprintFaction PlayerFaction
        {
            get
            {
                var f = BlueprintRoot.Instance?.PlayerFaction;
                if (f != null) return f;
                var mc = Game.Instance?.Player?.MainCharacter.Value;
                return mc?.Faction;
            }
        }

        private static bool Hostile(BlueprintFaction f, BlueprintFaction player)
        {
            if (f == null) return false;
            if (f.AlwaysEnemy || f.EnemyForEveryone) return true;
            if (player == null) return false;
            foreach (var a in f.AttackFactions) if (a == player) return true;
            return false;
        }

        private static void Rescan()
        {
            _spawners.Clear();
            var player = PlayerFaction;
            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll(typeof(UnitSpawnerBase)); }
            catch (Exception e) { Main.Log?.Warning("[strategic] spawner scan failed: " + e.Message); return; }
            foreach (var o in all)
            {
                var s = o as UnitSpawnerBase;
                if (s == null || s is CompanionSpawner) continue;
                if (!s.gameObject.scene.IsValid()) continue; // prefabs / assets, not the loaded scene
                BlueprintUnit bp;
                try { bp = s.Blueprint; } catch { continue; }
                if (bp == null || !Hostile(bp.Faction, player)) continue;
                _spawners.Add(new SpawnerRec { Spawner = s, Position = s.transform.position, Faction = bp.Faction });
            }
            RefreshSpawnerState();
            BuildClusters();
        }

        private static void RefreshSpawnerState()
        {
            _hostilePresent.Clear();
            for (int i = 0; i < _spawners.Count; i++)
            {
                var r = _spawners[i];
                bool pending = false, alive = false;
                try
                {
                    var s = r.Spawner;
                    if (s == null) { r.Pending = false; r.Alive = false; continue; }
                    pending = !s.HasSpawned;
                    if (!pending)
                    {
                        var u = s.SpawnedUnit;
                        alive = u != null && u.IsInGame && !u.State.IsDead;
                    }
                }
                catch { }
                r.Pending = pending; r.Alive = alive;
                if ((pending || alive) && r.Faction != null) _hostilePresent.Add(r.Faction);
            }
        }

        // Greedy centroid clustering on XZ: spawners in position order join the nearest cluster whose
        // centroid lies within ClusterRadius, else start one — so a pack's spread stays bounded
        // (single-linkage chained a whole wall line of spawners into one 17 m blob). Cluster
        // identity = the sorted member instance ids, so an unchanged pack keeps its ScanItem.
        private static void BuildClusters()
        {
            var order = new List<SpawnerRec>(_spawners);
            order.Sort((a, b) =>
            {
                int c = a.Position.x.CompareTo(b.Position.x);
                return c != 0 ? c : a.Position.z.CompareTo(b.Position.z);
            });
            var groups = new List<List<SpawnerRec>>();
            var centroids = new List<Vector3>();
            float r2 = ClusterRadius * ClusterRadius;
            foreach (var rec in order)
            {
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < groups.Count; i++)
                {
                    float dx = centroids[i].x - rec.Position.x, dz = centroids[i].z - rec.Position.z;
                    float d = dx * dx + dz * dz;
                    if (d <= r2 && d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) { groups.Add(new List<SpawnerRec> { rec }); centroids.Add(rec.Position); continue; }
                var g = groups[best];
                g.Add(rec);
                centroids[best] = centroids[best] + (rec.Position - centroids[best]) / g.Count;
            }
            var seen = new HashSet<string>();
            foreach (var g in groups)
            {
                var ids = new List<int>(g.Count);
                foreach (var r in g) ids.Add(r.Spawner.GetInstanceID());
                ids.Sort();
                string key = string.Join(",", ids);
                seen.Add(key);
                if (!_clusters.TryGetValue(key, out var c)) _clusters[key] = c = new SpawnCluster(key);
                c.SetMembers(g);
            }
            // Clusters whose member set changed (a rescan after a scene streamed in) are replaced.
            var stale = new List<string>();
            foreach (var k in _clusters.Keys) if (!seen.Contains(k)) stale.Add(k);
            foreach (var k in stale) _clusters.Remove(k);
            RebuildLiveClusters();
        }

        private static void RebuildLiveClusters()
        {
            _liveClusters.Clear();
            foreach (var c in _clusters.Values) if (c.Pending > 0) _liveClusters.Add(c);
        }

        // ---- the per-second pass ----

        private static void Refresh(Game game)
        {
            RefreshSpawnerState();
            RebuildLiveClusters();

            // Objectives: alive never-fight units some present hostile faction wants to attack. The
            // game plants several marker units per building ("Tavern" x4); one item per NAME, spanning
            // its live members, so the cycle says each objective once.
            foreach (var o in _objectives.Values) o.BeginRefresh();
            if (_hostilePresent.Count > 0)
                foreach (var u in game.State.Units)
                {
                    if (!IsObjective(u)) continue;
                    string name = u.CharacterName ?? "";
                    if (!_objectives.TryGetValue(name, out var item)) _objectives[name] = item = new ObjectiveItem(name);
                    item.AddMember(u);
                }
            _goneKeys.Clear();
            foreach (var kv in _objectives) { kv.Value.EndRefresh(); if (kv.Value.Members == 0) _goneKeys.Add(kv.Key); }
            foreach (var k in _goneKeys) _objectives.Remove(k);
            _liveObjectives.Clear();
            foreach (var o in _objectives.Values) _liveObjectives.Add(o);
        }

        private static bool IsObjective(UnitEntityData u)
        {
            try
            {
                if (u == null || !u.IsInGame || u.State.IsDead || u.IsPlayerFaction) return false;
                var f = u.Faction;
                if (f == null || !f.NeverJoinCombat) return false; // real combatants are Units, not objectives
                foreach (var h in _hostilePresent)
                    foreach (var a in h.AttackFactions)
                        if (a == f) return true;
            }
            catch { }
            return false;
        }

        // ---- items ----

        /// <summary>A spawn point: the centroid of a pack of pending hostile spawners.</summary>
        internal sealed class SpawnCluster : ScanItem
        {
            public readonly string Key;
            private readonly List<SpawnerRec> _members = new List<SpawnerRec>();
            private Vector3 _centroid;
            private float _reach;

            public SpawnCluster(string key) { Key = key; }

            internal void SetMembers(List<SpawnerRec> members)
            {
                _members.Clear(); _members.AddRange(members);
                var sum = Vector3.zero;
                foreach (var m in _members) sum += m.Position;
                _centroid = _members.Count > 0 ? sum / _members.Count : Vector3.zero;
                float r = 0f;
                foreach (var m in _members)
                {
                    float dx = m.Position.x - _centroid.x, dz = m.Position.z - _centroid.z;
                    r = Mathf.Max(r, Mathf.Sqrt(dx * dx + dz * dz));
                }
                _reach = r + 1f; // the pack's spread, so distance reads to its near edge
            }

            /// <summary>Spawners in this pack still to fire.</summary>
            public int Pending
            {
                get { int n = 0; for (int i = 0; i < _members.Count; i++) if (_members[i].Pending) n++; return n; }
            }

            public override string Name => Loc.T("scan.spawn_point", new { count = Pending });
            public override Vector3 Position => _centroid;
            public override float Footprint => _reach;
            public override ScanBounds Bounds
            {
                get
                {
                    var pts = new List<Vector3>(_members.Count);
                    foreach (var m in _members) pts.Add(m.Position);
                    return pts.Count > 0 ? ScanBounds.Cloud(_centroid, pts) : ScanBounds.Point(_centroid);
                }
            }
            public override Vector3 NearestPoint(Vector3 from)
            {
                Vector3 best = _centroid; float bestD = float.MaxValue;
                for (int i = 0; i < _members.Count; i++)
                {
                    var p = _members[i].Position;
                    float dx = p.x - from.x, dz = p.z - from.z, d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
            public override bool IsVisible => true;     // a hint, not a fog-gated thing
            public override bool CurrentlySeen => true;
            public override IEnumerable<string> Nodes { get { yield return ScanTaxonomy.StrategicSpawns; } }
            public override string Primary => ScanTaxonomy.StrategicSpawns;
        }

        /// <summary>An enemy objective: the marker units hostile AI runs at, grouped by their game name
        /// ("Tavern"). Position = the live members' centroid, footprint = their spread, so distance
        /// reads to the nearest one and the cursor lands among them.</summary>
        internal sealed class ObjectiveItem : ScanItem
        {
            private readonly string _name;
            private readonly List<UnitEntityData> _members = new List<UnitEntityData>();
            private Vector3 _centroid;
            private float _reach = 1f;

            public ObjectiveItem(string name) { _name = name; }

            public int Members => _members.Count;
            internal void BeginRefresh() { _members.Clear(); }
            internal void AddMember(UnitEntityData u) { _members.Add(u); }
            internal void EndRefresh()
            {
                if (_members.Count == 0) return;
                var sum = Vector3.zero;
                foreach (var m in _members) sum += Geo.Live(m);
                _centroid = sum / _members.Count;
                float r = 0f;
                foreach (var m in _members)
                {
                    var p = Geo.Live(m);
                    float dx = p.x - _centroid.x, dz = p.z - _centroid.z;
                    r = Mathf.Max(r, Mathf.Sqrt(dx * dx + dz * dz));
                }
                _reach = r + 1f;
            }

            public override string Name => _name;
            public override Vector3 Position => _centroid;
            public override float Footprint => _reach;
            // Distance/bearing read to the NEAREST marker, not the group's circle (the tavern's four
            // markers ring the building; a circle over them said "here" from inside it).
            public override ScanBounds Bounds
            {
                get
                {
                    var pts = new List<Vector3>(_members.Count);
                    foreach (var m in _members) pts.Add(Geo.Live(m));
                    return pts.Count > 0 ? ScanBounds.Cloud(_centroid, pts) : ScanBounds.Point(_centroid);
                }
            }
            public override Vector3 NearestPoint(Vector3 from)
            {
                Vector3 best = _centroid; float bestD = float.MaxValue;
                for (int i = 0; i < _members.Count; i++)
                {
                    var p = Geo.Live(_members[i]);
                    float dx = p.x - from.x, dz = p.z - from.z, d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
            public override bool IsVisible => true;
            public override bool CurrentlySeen => true;
            public override IEnumerable<string> Nodes { get { yield return ScanTaxonomy.StrategicObjectives; } }
            public override string Primary => ScanTaxonomy.StrategicObjectives;

            protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
                => NameAndType(Name, Loc.T("scan.objective_type"));
        }
    }
}
