using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using WrathAccess.Localization;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Curated NAMES for scene objects whose only handle is the designer's GameObject name. Most props
    /// read fine through ProxyMapObject.CleanName ("Bag", "Jug"), but puzzle furniture is named for
    /// the level designer, not the player: the Shield Maze combination locks light one
    /// "Rune_Torture_2_Yellow_1" per correct press, and "Rune Torture 2 Yellow 1" tells a blind player
    /// nothing about what a sighted one sees (the first slot's yellow rune glowing). So
    /// <c>assets/descriptions/&lt;Area&gt;.json</c> may carry
    /// <c>"names": [{ "match": regex, "key": locale key, "group": ..., "shown": ..., "hidden": ...,
    /// "all_shown": ..., "all_hidden": ... }]</c>: the regex's NAMED GROUPS become template variables
    /// of the locale entries ("slot {slot}: {color} rune"), each value localized through
    /// <c>obj.word.&lt;value&gt;</c> when such an entry exists (colour words), raw otherwise. The
    /// optional keys phrase the shown/hidden events ("… glows" / "… stops glowing") and, for objects
    /// sharing a <c>group</c>, the ONE line spoken when several change at once (a failed combination
    /// wipes every rune in the same frame: "all runes stop glowing" instead of four overlapping lines).
    /// </summary>
    internal static class ObjectNames
    {
        internal sealed class Def
        {
            public string match { get; set; }
            public string key { get; set; }
            public string group { get; set; }
            public string shown { get; set; }
            public string hidden { get; set; }
            public string all_shown { get; set; }
            public string all_hidden { get; set; }
            [JsonIgnore] public Regex Regex;
        }
        private sealed class AreaFile { public List<Def> names { get; set; } }

        /// <summary>One object's resolved naming: its definition plus the variables its name bound.</summary>
        internal sealed class Naming
        {
            public readonly Def Def;
            public readonly Dictionary<string, Message> Vars;
            public Naming(Def def, Dictionary<string, Message> vars) { Def = def; Vars = vars; }

            public string Name => Message.Localized("ui", Def.key, Vars).Resolve();
            public string Group => Def.group;
            /// <summary>The event line for this object appearing/vanishing, or null to use the generic one.</summary>
            public Message Change(bool shown)
            {
                var k = shown ? Def.shown : Def.hidden;
                return string.IsNullOrEmpty(k) ? null : Message.Localized("ui", k, Vars);
            }
            /// <summary>The ONE line for several objects of this group appearing/vanishing together, or null.</summary>
            public Message ChangeAll(bool shown)
            {
                var k = shown ? Def.all_shown : Def.all_hidden;
                return string.IsNullOrEmpty(k) || string.IsNullOrEmpty(Def.group) ? null : Message.Localized("ui", k, Vars);
            }
        }

        private static readonly List<Def> _defs = new List<Def>();
        private static string _loadedArea;

        /// <summary>Reload the area's curated names when the area changes.</summary>
        public static void Refresh(string areaName)
        {
            if (areaName == _loadedArea) return;
            _loadedArea = areaName;
            _defs.Clear();
            if (string.IsNullOrEmpty(areaName)) return;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "descriptions", areaName + ".json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<AreaFile>(File.ReadAllText(path));
                if (parsed?.names != null)
                    foreach (var d in parsed.names)
                    {
                        if (d == null || string.IsNullOrEmpty(d.match) || string.IsNullOrEmpty(d.key)) continue;
                        try { d.Regex = new Regex(d.match, RegexOptions.CultureInvariant); }
                        catch (Exception e) { Main.Log?.Warning("[desc] bad name pattern " + d.match + ": " + e.Message); continue; }
                        _defs.Add(d);
                    }
                if (_defs.Count > 0) Main.Log?.Log("[desc] " + areaName + ": " + _defs.Count + " object name patterns");
            }
            catch (Exception e) { Main.Log?.Warning("[desc] names load failed for " + areaName + ": " + e.Message); }
        }

        public static bool Any => _defs.Count > 0;

        /// <summary>The curated naming for a scene object name, or null. Runs the patterns (allocates) —
        /// resolve ONCE per entity and cache.</summary>
        public static Naming Resolve(string sceneName)
        {
            if (_defs.Count == 0 || string.IsNullOrEmpty(sceneName)) return null;
            var name = sceneName.Replace("(Clone)", "").Trim();
            foreach (var d in _defs)
            {
                var m = d.Regex.Match(name);
                if (!m.Success) continue;
                var vars = new Dictionary<string, Message>();
                foreach (var g in d.Regex.GetGroupNames())
                {
                    if (int.TryParse(g, out _)) continue; // numbered groups aren't variables
                    var v = m.Groups[g].Value;
                    // A word the locale knows ("Yellow" → the translated colour) else the raw capture.
                    vars[g] = Message.Raw(LocalizationManager.GetOrDefault("ui", "obj.word." + v.ToLowerInvariant(), v));
                }
                return new Naming(d, vars);
            }
            return null;
        }
    }
}
