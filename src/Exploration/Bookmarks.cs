using System;
using System.Collections.Generic;
using System.IO;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Newtonsoft.Json;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>One saved bookmark (the JSON row). Two kinds: a POINT (a bare world position) and an
    /// ATTACHED bookmark (pinned to a game entity by its stable UniqueId, with the position as the
    /// fallback should the entity be gone). Keyed to an area part, so each area shows only its own.</summary>
    internal sealed class Bookmark
    {
        public string Id;
        public string Label;
        public string AreaKey;      // "<area blueprint>|<part>"
        public string Kind;         // "point" | "attached"
        public float X, Y, Z;
        public string EntityId;     // attached: EntityDataBase.UniqueId
        public string TargetName;   // attached: the target's name at pin time (the fallback wording)

        [JsonIgnore] public Vector3 Position => new Vector3(X, Y, Z);
        [JsonIgnore] public bool Attached => Kind == "attached";
    }

    /// <summary>
    /// The player's own BOOKMARKS — a scanner category ("Bookmarks": point / attached) for finding the
    /// way back to places and things. Alt+B opens <see cref="Screens.BookmarkScreen"/> to pin the
    /// current position or the reviewed scanner object under a label; the category then lists them
    /// with the usual bearing/distance, Slash plants the cursor, and an attached bookmark follows its
    /// entity (and interacts as it does). Persisted in the mod's data folder beside settings.json,
    /// per area part, across sessions and saves (they're the player's notes, not game state).
    /// </summary>
    internal static class BookmarkModel
    {
        private sealed class File { public List<Bookmark> Bookmarks = new List<Bookmark>(); }

        private static readonly List<Bookmark> _all = new List<Bookmark>();
        private static readonly List<BookmarkItem> _current = new List<BookmarkItem>();
        private static readonly Dictionary<string, BookmarkItem> _items = new Dictionary<string, BookmarkItem>();
        private static string _areaKey;
        private static bool _loaded;
        private static int _version, _builtVersion = -1;

        private static string PathFile
            => System.IO.Path.Combine(Application.persistentDataPath, "WrathAccess", "bookmarks.json");

        /// <summary>The current area part's bookmarks as scan items (stable identity per bookmark).</summary>
        public static IReadOnlyList<BookmarkItem> Current => _current;

        public static string CurrentAreaKey
        {
            get
            {
                var area = Game.Instance?.CurrentlyLoadedArea;
                if (area == null) return null;
                var part = Kingmaker.Blueprints.Area.AreaService.Instance?.CurrentAreaPart;
                return area.name + "|" + (part != null ? part.name : "");
            }
        }

        /// <summary>Per-tick (from WorldModel.Tick): follow area changes, rebuild the item list on change.</summary>
        public static void Tick()
        {
            EnsureLoaded();
            string key = CurrentAreaKey;
            if (key != _areaKey) { _areaKey = key; _builtVersion = -1; }
            if (_builtVersion == _version) return;
            _builtVersion = _version;
            _current.Clear();
            if (key == null) return;
            var seen = new HashSet<string>();
            foreach (var bm in _all)
            {
                if (bm.AreaKey != key) continue;
                seen.Add(bm.Id);
                if (!_items.TryGetValue(bm.Id, out var item)) _items[bm.Id] = item = new BookmarkItem(bm);
                _current.Add(item);
            }
            var stale = new List<string>();
            foreach (var k in _items.Keys) if (!seen.Contains(k)) stale.Add(k);
            foreach (var k in stale) _items.Remove(k);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var p = PathFile;
                if (System.IO.File.Exists(p))
                {
                    var f = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(p));
                    if (f?.Bookmarks != null)
                        foreach (var b in f.Bookmarks)
                            if (b != null && !string.IsNullOrEmpty(b.Id)) _all.Add(b);
                }
                Main.Log?.Log("[bookmarks] loaded " + _all.Count);
            }
            catch (Exception e) { Main.Log?.Warning("[bookmarks] load failed: " + e.Message); }
        }

        private static void Save()
        {
            try
            {
                var p = PathFile;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                System.IO.File.WriteAllText(p, JsonConvert.SerializeObject(new File { Bookmarks = _all }, Formatting.Indented));
            }
            catch (Exception e) { Main.Log?.Warning("[bookmarks] save failed: " + e.Message); }
        }

        private static string DefaultLabel()
        {
            int n = 0;
            foreach (var b in _all) if (b.AreaKey == CurrentAreaKey) n++;
            return Loc.T("bookmark.default_label", new { n = n + 1 });
        }

        /// <summary>Pin a bare position. Empty label → "Bookmark N".</summary>
        public static Bookmark AddPoint(string label, Vector3 pos)
        {
            var bm = new Bookmark
            {
                Id = Guid.NewGuid().ToString("N"), Kind = "point",
                Label = string.IsNullOrEmpty(label) ? DefaultLabel() : label,
                AreaKey = CurrentAreaKey, X = pos.x, Y = pos.y, Z = pos.z,
            };
            _all.Add(bm); _version++; Save();
            return bm;
        }

        /// <summary>Pin a scanner item: an entity-backed one by its UniqueId (follows the entity), anything
        /// else (room exits, frontier blobs, hints) as a point at its position under its name.</summary>
        public static Bookmark AddAttached(string label, ScanItem target)
        {
            var ent = (target as ProxyEntity)?.EntityRef;
            var pos = target.Position;
            var bm = new Bookmark
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = ent != null ? "attached" : "point",
                Label = string.IsNullOrEmpty(label) ? (target.Name ?? DefaultLabel()) : label,
                AreaKey = CurrentAreaKey, X = pos.x, Y = pos.y, Z = pos.z,
                EntityId = ent?.UniqueId, TargetName = target.Name,
            };
            _all.Add(bm); _version++; Save();
            return bm;
        }

        public static void Remove(Bookmark bm)
        {
            if (_all.Remove(bm)) { _version++; Save(); }
        }

        /// <summary>The entity-backed scan item with this UniqueId, or null (gone / not in this part).</summary>
        internal static ScanItem FindEntity(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId)) return null;
            foreach (var it in WorldModel.Items)
            {
                var pe = it as ProxyEntity;
                if (pe != null && pe.EntityRef != null && pe.EntityRef.UniqueId == uniqueId) return it;
            }
            return null;
        }
    }

    /// <summary>A bookmark as a scanner item. An attached one resolves its entity live (re-looked-up
    /// every couple of seconds; the entity's proxy is the source for position, bounds, unit and
    /// interaction), falling back to the pinned position and name when the entity is gone.</summary>
    internal sealed class BookmarkItem : ScanItem
    {
        public readonly Bookmark Bookmark;
        private ScanItem _target;
        private float _nextResolve;

        public BookmarkItem(Bookmark bm) { Bookmark = bm; }

        private ScanItem Target
        {
            get
            {
                if (!Bookmark.Attached) return null;
                float now = Time.unscaledTime;
                if (_target == null || now >= _nextResolve)
                {
                    _nextResolve = now + 2f;
                    _target = BookmarkModel.FindEntity(Bookmark.EntityId);
                }
                return _target;
            }
        }

        public override string Name => Bookmark.Label;
        public override Vector3 Position => Target?.Position ?? Bookmark.Position;
        public override float Footprint => Target?.Footprint ?? 0f;
        public override ScanBounds Bounds => Target?.Bounds ?? ScanBounds.Point(Bookmark.Position);
        public override Vector3 NearestPoint(Vector3 from) => Target != null ? Target.NearestPoint(from) : Bookmark.Position;
        public override UnitEntityData TargetUnit => Target?.TargetUnit;
        public override bool IsVisible => true;      // the player's own note — never fog-gated
        public override bool CurrentlySeen => true;
        public override InteractOutcome Interact() => Target != null ? Target.Interact() : InteractOutcome.NotSupported;

        public override IEnumerable<string> Nodes
        {
            get { yield return Bookmark.Attached ? ScanTaxonomy.BookmarksAttached : ScanTaxonomy.BookmarksPoint; }
        }
        public override string Primary => Bookmark.Attached ? ScanTaxonomy.BookmarksAttached : ScanTaxonomy.BookmarksPoint;

        // "Label, bookmark" / "Label, bookmark on Door" (the live target's name, else the pinned one).
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            string type = Bookmark.Attached
                ? Loc.T("bookmark.on", new { name = Target?.Name ?? Bookmark.TargetName ?? "" })
                : Loc.T("bookmark.point");
            return NameAndType(Name, type);
        }
    }
}
