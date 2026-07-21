using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The user's per-entity-type SWITCHES over the <see cref="ScanTaxonomy"/> tree — two independent
    /// questions the old "pick Silent" workflow conflated:
    /// <list type="bullet">
    /// <item>LISTED (<c>scan_enabled.*</c>) — does this kind of thing participate in the scanner
    /// (category/subcategory lists, Everything, the review-cursor cycles)?</item>
    /// <item>PLAY SOUND (<c>scan_sound.*</c>) — does the sonar ping it? Independent of Listed and of
    /// WHICH sound is picked (<see cref="ScanSounds"/>): the pick chooses the voice, this mutes it.</item>
    /// </list>
    /// Each is one BoolSetting per category (default on) plus an inherit-capable tri-state per
    /// subcategory, mirroring the sound tree. Scenery and points of interest stay outside the system
    /// (their listing rules are their own); the world-map taxonomy is untouched. Individually selected
    /// items always announce — Listed gates discovery surfaces only.
    /// </summary>
    internal static class ScanEnabled
    {
        /// <summary>Build both settings trees (called from Main next to the sound registration).</summary>
        public static void RegisterSettings()
        {
            // All toggles labelled by their QUESTION ("Listed" / "Play sound") — they render inside
            // their taxonomy node's group, whose header already names the entity type.
            ModSettings.Root.Add(BuildTree("scan_enabled", "Listed entity types", "scanner.listed_types",
                "Listed", "scanner.listed"));
            ModSettings.Root.Add(BuildTree("scan_sound", "Sounding entity types", "scanner.sounding_types",
                "Play sound", "scanner.play_sound"));
        }

        private static CategorySetting BuildTree(string rootKey, string rootLabel, string rootLoc,
            string label, string loc)
        {
            var root = new CategorySetting(rootKey, rootLabel, localizationKey: rootLoc);
            foreach (var cat in ScanTaxonomy.Categories)
            {
                if (!Participates(cat.Key)) continue;
                var catEnabled = new BoolSetting(cat.Key, label, true, loc);
                root.Add(catEnabled);
                foreach (var child in cat.Children)
                    root.Add(new NullableBoolSetting(ChildKey(child.Key), label, catEnabled, loc));
            }
            return root;
        }

        // Scenery/POI listing rules are bespoke (scenery is silent-by-default browsing, POI a curated
        // duplicate cycle) — they never joined this system.
        private static bool Participates(string catKey) => catKey != "scenery" && catKey != "poi";

        // Flattened child path: "units.party" -> "units_party" (settings keys are dot-separated paths).
        private static string ChildKey(string nodeKey) => nodeKey.Replace('.', '_');

        /// <summary>The setting behind a node's Listed toggle (BoolSetting for categories,
        /// NullableBoolSetting for subcategories), or null for nodes outside the system.</summary>
        public static Setting EnabledSetting(string nodeKey) => Find("scan_enabled", nodeKey);

        /// <summary>The setting behind a node's Play-sound toggle (same shapes as Listed).</summary>
        public static Setting SoundEnabledSetting(string nodeKey) => Find("scan_sound", nodeKey);

        /// <summary>Is this taxonomy node listed? Subcategories inherit their category unless
        /// overridden; nodes outside the system (scenery, POI, world map) are always listed.</summary>
        public static bool Enabled(string nodeKey) => Resolve(EnabledSetting(nodeKey));

        /// <summary>Should the sonar ping this taxonomy node (given its pick isn't Silent)?</summary>
        public static bool SoundEnabled(string nodeKey) => Resolve(SoundEnabledSetting(nodeKey));

        private static Setting Find(string rootKey, string nodeKey)
        {
            var node = ScanTaxonomy.Get(nodeKey);
            if (node == null) return null;
            string key = node.IsCategory ? node.Key : ChildKey(node.Key);
            return (Setting)ModSettings.GetSetting<BoolSetting>(rootKey + "." + key)
                ?? ModSettings.GetSetting<NullableBoolSetting>(rootKey + "." + key);
        }

        private static bool Resolve(Setting s)
        {
            switch (s)
            {
                case NullableBoolSetting nb: return nb.Resolved;
                case BoolSetting b: return b.Get();
                default: return true;
            }
        }
    }
}
