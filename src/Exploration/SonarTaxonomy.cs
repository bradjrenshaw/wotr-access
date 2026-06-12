using System;
using System.Collections.Generic;
using System.IO;
using WrathAccess.Exploration.Overlays;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The shared category/subcategory taxonomy for area things — ONE tree that the scanner, sonar,
    /// object cues, and the settings UI all follow. Each <see cref="ScanItem"/> reports its full
    /// category memberships (<see cref="ScanItem.Categories"/>, the scanner lists it under every one)
    /// plus a single state-aware <see cref="ScanItem.Primary"/> node — the one that SOUNDS. Which
    /// sound (or silence) each node plays is the user's: a global "sounds" settings tree (like the
    /// shared volumes — sound identity isn't per-overlay), one dropdown per node over the wav files in
    /// assets/audio/interactables, with subcategories able to INHERIT their parent category's pick.
    /// </summary>
    internal static class SonarTaxonomy
    {
        // Node keys (what ScanItem.Primary returns). Containers and Doors are branches with children.
        public const string Poi = "poi";
        public const string Party = "party";
        public const string Enemies = "enemies";
        public const string Neutrals = "neutrals";
        public const string Doors = "doors";
        public const string DoorsOpen = "doors.open";
        public const string Containers = "containers";
        public const string ContainersChest = "containers.chest";
        public const string ContainersCorpse = "containers.corpse";
        public const string ContainersEnvironment = "containers.environment";
        public const string ContainersSingle = "containers.single";
        public const string ContainersStash = "containers.stash";
        public const string ContainersOther = "containers.other";
        public const string Exits = "exits";
        public const string SearchPoints = "searchpoints";
        public const string Traps = "traps";
        public const string Mechanisms = "mechanisms";
        public const string Scenery = "scenery";

        private const string Silent = "silent";
        private const string Inherit = "inherit";

        // key, parent (null = top level), English fallback label, loc key, default choice id.
        private static readonly (string key, string parent, string label, string loc, string def)[] Nodes =
        {
            (Poi, null, "Points of interest", "taxonomy.poi", Silent),
            (Party, null, "Party", "taxonomy.party", "units-ally"),
            (Enemies, null, "Enemies", "taxonomy.enemies", "units-enemy"),
            (Neutrals, null, "Neutrals", "taxonomy.neutrals", "units-neutral"),
            (Doors, null, "All doors", "taxonomy.doors.all", "door"),
            (DoorsOpen, Doors, "Open doors", "taxonomy.doors.open", "door_open"),
            (Containers, null, "All containers", "taxonomy.containers.all", "loot-generic"),
            (ContainersChest, Containers, "Chests", "taxonomy.containers.chest", "loot-chest"),
            (ContainersCorpse, Containers, "Corpses", "taxonomy.containers.corpse", "loot-corpse"),
            (ContainersEnvironment, Containers, "Environment", "taxonomy.containers.environment", "loot-environment"),
            (ContainersSingle, Containers, "Single slot", "taxonomy.containers.single", "loot-single"),
            (ContainersStash, Containers, "Player chest", "taxonomy.containers.stash", "loot-stash"),
            (ContainersOther, Containers, "Other containers", "taxonomy.containers.other", "loot-generic"),
            (Exits, null, "Exits", "taxonomy.exits", "transition"),
            (SearchPoints, null, "Search points", "taxonomy.searchpoints", "unknown"),
            (Traps, null, "Traps", "taxonomy.traps", "trap"),
            (Mechanisms, null, "Mechanisms", "taxonomy.mechanisms", "mechanism"),
            (Scenery, null, "Scenery", "taxonomy.scenery", Silent),
        };

        /// <summary>True for nodes that mark a real interactive thing (cursor targeting cares about
        /// these regardless of what sound — if any — the user assigned them).</summary>
        public static bool IsInteractive(string key)
            => key != null && key != Poi && key != Scenery;

        /// <summary>The wav stems available for assignment (assets/audio/interactables/*.wav).</summary>
        private static List<string> AvailableSounds()
        {
            try
            {
                var dir = Path.Combine(OverlayAudio.Dir, "interactables");
                var stems = new List<string>();
                foreach (var f in Directory.GetFiles(dir, "*.wav"))
                    stems.Add(Path.GetFileNameWithoutExtension(f));
                stems.Sort(StringComparer.OrdinalIgnoreCase);
                return stems;
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[taxonomy] couldn't list interactable sounds: " + e.Message);
                return new List<string>();
            }
        }

        /// <summary>Build the global "sounds" settings tree (called from Main.BuildSettings): one
        /// dropdown per taxonomy node; child nodes additionally offer Inherit (use the parent's pick).
        /// Rendered on the Sonar tab of the mod menu.</summary>
        public static void RegisterSettings()
        {
            var stems = AvailableSounds();
            var root = new CategorySetting("sounds", "Sounds", localizationKey: "sounds.title");
            var byKey = new Dictionary<string, CategorySetting> { { "", root } };

            foreach (var n in Nodes)
            {
                var choices = new List<Choice>();
                if (n.parent != null) choices.Add(new Choice(Inherit, "Inherit", "choice.inherit"));
                choices.Add(new Choice(Silent, "Silent", "choice.silent"));
                foreach (var s in stems) choices.Add(new Choice(s, s, "sound." + s));
                // A default that vanished from disk (user removed a wav) falls back to silent.
                var def = n.def == Silent || stems.Contains(n.def) ? n.def : Silent;

                if (n.parent == null && IsBranch(n.key))
                {
                    // A branch node: its own pick lives as "all" inside a nested category so its
                    // children render beneath it as one collapsible group.
                    var cat = new CategorySetting(n.key, n.label, localizationKey: "taxonomy." + n.key);
                    cat.Add(new ChoiceSetting("all", n.label, choices, def, n.loc));
                    root.Add(cat);
                    byKey[n.key] = cat;
                }
                else if (n.parent != null)
                {
                    byKey[n.parent].Add(new ChoiceSetting(LeafKey(n.key), n.label, choices, def, n.loc));
                }
                else
                {
                    root.Add(new ChoiceSetting(n.key, n.label, choices, def, n.loc));
                }
            }

            ModSettings.Root.Add(root);
        }

        private static bool IsBranch(string key)
        {
            foreach (var n in Nodes)
                if (n.parent == key) return true;
            return false;
        }

        private static string LeafKey(string key)
        {
            int dot = key.LastIndexOf('.');
            return dot < 0 ? key : key.Substring(dot + 1);
        }

        // Settings path for a node's dropdown: top level "sounds.<key>"; a branch's own pick
        // "sounds.<key>.all"; a child "sounds.<parent>.<leaf>".
        private static string PathFor(string key)
        {
            if (key.IndexOf('.') >= 0) return "sounds." + key;
            return IsBranch(key) ? "sounds." + key + ".all" : "sounds." + key;
        }

        private static string ParentOf(string key)
        {
            foreach (var n in Nodes)
                if (n.key == key) return n.parent;
            return null;
        }

        /// <summary>The wav stem the given taxonomy node should play, honouring Inherit, or null for
        /// silent/unknown nodes. Read live (settings dictionary lookups — cheap enough per ping).</summary>
        public static string Resolve(string nodeKey)
        {
            for (int guard = 0; nodeKey != null && guard < 4; guard++)
            {
                var setting = ModSettings.GetSetting<ChoiceSetting>(PathFor(nodeKey));
                if (setting == null) return null;
                var id = setting.ValueId;
                if (id == Silent) return null;
                if (id != Inherit) return id;
                nodeKey = ParentOf(nodeKey);
            }
            return null;
        }
    }
}
