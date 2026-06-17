using System.Collections.Generic;

namespace WrathAccess.Exploration
{
    /// <summary>Which announcement part-set a category uses (set on the category; subcategories inherit).</summary>
    internal enum ScanClass { Unit, Object, Marker }

    /// <summary>
    /// THE canonical two-level classification of scannable things — one tree that drives scanner
    /// navigation, sonar sounds, announcements, and the settings UI. Replaces the old split between the
    /// flat <c>ScanCategory</c> enum (nav) and the half-nested <c>SonarTaxonomy</c> (sounds).
    ///
    /// Shape: top-level CATEGORIES (Units, Containers, Doors, …), each with zero or more SUBCATEGORIES
    /// (Units → Party/Enemies/Neutrals; Containers → Chests/Corpses/…). Every category also has an implicit
    /// "All" entry (the category node itself) — browsing the union and serving as the inherit base for its
    /// children. Node keys are dotted: "units", "units.enemies", "containers.corpse". An item reports the
    /// (sub)categories it belongs to (many-to-many), one state-aware node that SOUNDS, and one stable node
    /// it ANNOUNCES as; all three are keys into this one tree.
    ///
    /// Phase 1: the data model + structural helpers only — nothing reads it yet. Settings-coupled
    /// resolution (sound-with-inherit, per-node announcement overrides) lands when those systems are
    /// rewired onto it; <c>SonarTaxonomy</c> / <c>ScanCategory</c> are removed then.
    /// </summary>
    internal static class ScanTaxonomy
    {
        /// <summary>A sound pick meaning "play nothing".</summary>
        public const string Silent = "silent";
        /// <summary>A child sound pick meaning "use the parent category's pick".</summary>
        public const string Inherit = "inherit";

        internal sealed class Node
        {
            public string Key { get; }            // dotted full key: "units", "units.enemies"
            public string Label { get; }          // English fallback (localised via LocKey)
            public string DefaultSound { get; }    // wav stem, or Silent
            public Node Parent { get; internal set; }
            public List<Node> Children { get; } = new List<Node>();

            private readonly ScanClass? _class;     // set on categories; subcategories inherit the parent's

            public Node(string key, string label, string defaultSound, ScanClass? cls)
            {
                Key = key; Label = label; DefaultSound = defaultSound; _class = cls;
            }

            public bool IsCategory => Parent == null;
            public bool IsBranch => Children.Count > 0;

            /// <summary>Loc key for the spoken/settings label ("taxonomy.units", "taxonomy.units.enemies").</summary>
            public string LocKey => "taxonomy." + Key;

            /// <summary>The announcement part-set class — the category's own, inherited by subcategories.</summary>
            public ScanClass Class => _class ?? Parent?.Class ?? ScanClass.Object;
        }

        private static readonly List<Node> _categories = new List<Node>();
        private static readonly Dictionary<string, Node> _byKey = new Dictionary<string, Node>();

        /// <summary>Top-level categories in navigation / display order.</summary>
        public static IReadOnlyList<Node> Categories => _categories;

        /// <summary>The node for a key, or null.</summary>
        public static Node Get(string key) => key != null && _byKey.TryGetValue(key, out var n) ? n : null;

        /// <summary>The subcategory cycle for a category: its "All" entry (the category itself) then its
        /// children. A leaf category yields just itself.</summary>
        public static IReadOnlyList<Node> NavSubcategories(Node category)
        {
            var list = new List<Node> { category };
            if (category != null) list.AddRange(category.Children);
            return list;
        }

        /// <summary>Every node, categories then their children, in declaration order (settings/locale walks).</summary>
        public static IEnumerable<Node> AllNodes()
        {
            foreach (var c in _categories)
            {
                yield return c;
                foreach (var s in c.Children) yield return s;
            }
        }

        static ScanTaxonomy()
        {
            Cat("units", "Units", ScanClass.Unit, Silent,
                Sub("party", "Party", "units-ally"),
                Sub("enemies", "Enemies", "units-enemy"),
                Sub("neutrals", "Neutrals", "units-neutral"));

            Cat("containers", "Containers", ScanClass.Object, "loot-generic",
                Sub("chest", "Chests", "loot-chest"),
                Sub("corpse", "Corpses", "loot-corpse"),
                Sub("environment", "Environment", "loot-environment"),
                Sub("single", "Single slot", "loot-single"),
                Sub("stash", "Player chest", "loot-stash"),
                Sub("other", "Other containers", "loot-generic"));

            Cat("doors", "Doors", ScanClass.Object, "door",
                Sub("open", "Open doors", "door_open"));

            Cat("exits", "Exits", ScanClass.Object, "transition");
            Cat("searchpoints", "Search points", ScanClass.Object, "unknown");
            Cat("traps", "Traps", ScanClass.Object, "trap");
            Cat("mechanisms", "Mechanisms", ScanClass.Object, "mechanism");
            Cat("scenery", "Scenery", ScanClass.Object, Silent);
            Cat("poi", "Points of interest", ScanClass.Marker, Silent);
        }

        // ---- builders ----

        // A pending subcategory (leaf name + label + default sound); the category prepends its key.
        private struct SubDef { public string Leaf, Label, Sound; }
        private static SubDef Sub(string leaf, string label, string sound)
            => new SubDef { Leaf = leaf, Label = label, Sound = sound };

        private static void Cat(string key, string label, ScanClass cls, string sound, params SubDef[] subs)
        {
            var cat = new Node(key, label, sound, cls);
            Register(cat);
            foreach (var s in subs)
            {
                var child = new Node(key + "." + s.Leaf, s.Label, s.Sound, null) { Parent = cat };
                cat.Children.Add(child);
                Register(child);
            }
            _categories.Add(cat);
        }

        private static void Register(Node n) => _byKey[n.Key] = n;
    }
}
