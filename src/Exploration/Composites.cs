using System;
using System.Collections.Generic;
using System.IO;
using Kingmaker.EntitySystem.Entities; // MapObjectEntityData
using Newtonsoft.Json;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Curated COMPOSITES: several scene map objects that are ONE thing to the player, folded into a
    /// single scan item. The Gray Garrison statue puzzle is the model case — each statue is a lever
    /// (its activation), a search point (a perception check at head height) and a glow marker (a
    /// light object the puzzle shows/hides), three objects 0.4m apart that cycled as three entries.
    /// Owlcat's scene authoring never marks them as related and they don't overlap exactly, so this
    /// is hand-curated per area: <c>assets/descriptions/&lt;Area&gt;.json</c> gains
    /// <c>"composites": [{ "key": ..., "members": [scene object names...] }]</c>. The FIRST member is
    /// the primary (position, sounding node, default interaction); the composite's spoken name is the
    /// locale entry <c>composite.&lt;key&gt;</c>. Members are matched by exact scene object name (the
    /// Unity name, "(Clone)" stripped) and drop out of the model as separate items; a member that
    /// isn't in the scene (a hidden glow) simply contributes nothing until it appears.
    /// </summary>
    internal static class Composites
    {
        internal sealed class Def
        {
            public string key { get; set; }
            public List<string> members { get; set; }
        }
        private sealed class AreaFile { public List<Def> composites { get; set; } }

        // member scene name → its composite (per loaded area)
        private static readonly Dictionary<string, CompositeItem> _byMember = new Dictionary<string, CompositeItem>(StringComparer.Ordinal);
        private static readonly List<CompositeItem> _all = new List<CompositeItem>();
        private static string _loadedArea;

        /// <summary>Reload the area's composites when the area changes. True when the set changed (the
        /// caller drops its per-entity classification caches).</summary>
        public static bool Refresh(string areaName)
        {
            if (areaName == _loadedArea) return false;
            _loadedArea = areaName;
            _byMember.Clear();
            _all.Clear();
            if (string.IsNullOrEmpty(areaName)) return true;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "descriptions", areaName + ".json");
                if (!File.Exists(path)) return true;
                var parsed = JsonConvert.DeserializeObject<AreaFile>(File.ReadAllText(path));
                if (parsed?.composites != null)
                    foreach (var d in parsed.composites)
                    {
                        if (d == null || string.IsNullOrEmpty(d.key) || d.members == null || d.members.Count == 0) continue;
                        var item = new CompositeItem(d);
                        _all.Add(item);
                        foreach (var m in d.members)
                            if (!string.IsNullOrEmpty(m) && !_byMember.ContainsKey(m)) _byMember[m] = item;
                    }
                if (_all.Count > 0) Main.Log?.Log("[desc] " + areaName + ": " + _all.Count + " composites");
            }
            catch (Exception e) { Main.Log?.Warning("[desc] composites load failed for " + areaName + ": " + e.Message); }
            return true;
        }

        public static bool Any => _all.Count > 0;

        /// <summary>The composite this map object belongs to, or null. Reads the Unity name (allocates)
        /// — call ONCE per entity and cache the answer.</summary>
        public static CompositeItem Classify(MapObjectEntityData o)
        {
            if (_byMember.Count == 0) return null;
            var view = o?.View;
            if (view == null) return null;
            var name = view.name;
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace("(Clone)", "").Trim();
            return _byMember.TryGetValue(name, out var item) ? item : null;
        }
    }

    /// <summary>One curated composite as a scan item: the union of its present members' roles, spoken
    /// as one line (name, then each member's type / check / state in authored order), interacting via
    /// the primary — or a choice of members when several are interactable.</summary>
    internal sealed class CompositeItem : ScanItem
    {
        private readonly Composites.Def _def;
        // present members by definition slot (null = not in the scene right now)
        private readonly ProxyMapObject[] _members;
        private readonly MapObjectEntityData[] _entities;
        private readonly List<string> _nodes = new List<string>();

        public CompositeItem(Composites.Def def)
        {
            _def = def;
            _members = new ProxyMapObject[def.members.Count];
            _entities = new MapObjectEntityData[def.members.Count];
        }

        public Composites.Def Definition => _def;

        /// <summary>A member object is in the scene this tick.</summary>
        public void Touch(MapObjectEntityData o)
        {
            var view = o?.View;
            if (view == null) return;
            for (int i = 0; i < _entities.Length; i++)
                if (ReferenceEquals(_entities[i], o)) return;
            var name = view.name.Replace("(Clone)", "").Trim();
            for (int i = 0; i < _def.members.Count; i++)
                if (_def.members[i] == name && _entities[i] == null)
                {
                    _entities[i] = o;
                    _members[i] = new ProxyMapObject(o);
                    return;
                }
        }

        /// <summary>Drop members that left the scene. True when any member remains.</summary>
        public bool Prune(HashSet<object> present)
        {
            bool any = false;
            for (int i = 0; i < _entities.Length; i++)
            {
                if (_entities[i] == null) continue;
                if (!present.Contains(_entities[i])) { _entities[i] = null; _members[i] = null; }
                else any = true;
            }
            return any;
        }

        private ProxyMapObject PrimaryMember
        {
            get
            {
                for (int i = 0; i < _members.Length; i++) if (_members[i] != null) return _members[i];
                return null;
            }
        }

        public override string Name => Loc.T("composite." + _def.key);
        public override Vector3 Position => PrimaryMember?.Position ?? Vector3.zero;
        public override string AssetKey => PrimaryMember?.AssetKey;

        public override IEnumerable<string> Nodes
        {
            get
            {
                _nodes.Clear();
                bool role = false;
                for (int i = 0; i < _members.Length; i++)
                {
                    var m = _members[i];
                    if (m == null) continue;
                    foreach (var n in m.Nodes)
                    {
                        if (n == ScanTaxonomy.Scenery) continue;
                        if (!_nodes.Contains(n)) { _nodes.Add(n); role = true; }
                    }
                }
                if (!role) _nodes.Add(ScanTaxonomy.Scenery);
                return _nodes;
            }
        }

        // Sounds/announces as the primary member's role; the primary is the first present member,
        // so a statue whose lever is gone would fall back to its search point.
        public override string Primary
        {
            get
            {
                for (int i = 0; i < _members.Length; i++)
                {
                    var p = _members[i]?.Primary;
                    if (p != null && p != ScanTaxonomy.Scenery) return p;
                }
                return PrimaryMember?.Primary;
            }
        }

        public override bool IsVisible
        {
            get
            {
                for (int i = 0; i < _members.Length; i++) if (_members[i] != null && _members[i].IsVisible) return true;
                return false;
            }
        }

        public override bool CurrentlySeen
        {
            get
            {
                for (int i = 0; i < _members.Length; i++) if (_members[i] != null && _members[i].CurrentlySeen) return true;
                return false;
            }
        }

        public override float Footprint
        {
            get
            {
                float r = 0f;
                for (int i = 0; i < _members.Length; i++) if (_members[i] != null) r = Mathf.Max(r, _members[i].Footprint);
                return r;
            }
        }

        // Name, then each present member's role in authored order: type word, check tag, state words.
        // Scenery members (a glow marker) add nothing — their meaning is folded into the primary's
        // state (the lever reads "lit").
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            yield return new Announce.NamePart(Name);
            for (int i = 0; i < _members.Length; i++)
            {
                var m = _members[i];
                if (m == null || !HasRole(m)) continue;
                var type = m.TypeWordText();
                if (!string.IsNullOrEmpty(type)) yield return new Announce.TypePart(type);
                var check = m.CheckTextText();
                if (check != null) yield return new Announce.CheckPart(check);
                var states = m.StateWordList();
                if (states.Count > 0) yield return new Announce.ObjectStatePart(states);
            }
        }

        private static bool HasRole(ProxyMapObject m)
        {
            foreach (var n in m.Nodes) if (n != ScanTaxonomy.Scenery) return true;
            return false;
        }

        // Enter: the one interactable member acts; several → pick which (their type words, with the
        // check tag so "Search point, Perception DC 20" tells them apart).
        public override InteractOutcome Interact()
        {
            var choices = new List<ProxyMapObject>();
            for (int i = 0; i < _members.Length; i++)
            {
                var m = _members[i];
                if (m != null && ScanTaxonomy.IsInteractive(m.Primary)) choices.Add(m);
            }
            if (choices.Count == 0) return InteractOutcome.NotSupported;
            if (choices.Count == 1) return choices[0].Interact();
            var labels = new List<string>();
            foreach (var m in choices)
            {
                var label = m.TypeWordText();
                var check = m.CheckTextText();
                labels.Add(check != null ? label + ", " + check : label);
            }
            Screens.ChoiceSubmenuScreen.Open(Name, labels, -1, i => choices[i].Interact());
            return InteractOutcome.RefusedSpoken; // the menu is the response; nothing to announce
        }
    }
}
