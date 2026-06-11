using System;
using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Builds the data-driven overlays from settings and supports adding/removing them at runtime.
    ///
    /// SETTINGS MODEL (the redesign): system TUNABLES are SHARED — registered once under
    /// <c>defaults.&lt;system&gt;</c> (surfaced by the Sonar / Log / Exploration tabs) with audio volumes
    /// under <c>audio.volumes</c> (Audio tab). An overlay's subtree (<c>overlays.&lt;id&gt;</c>) holds only
    /// COMPOSITION: a hidden display name, the cursor slots, and per system an <c>enabled</c> toggle +
    /// hidden <c>customized</c> flag. Whole-subtree inheritance: Customize() materializes the overlay's own
    /// full copy of a system's tree (same RegisterSettings schema, seeded from the current defaults) under
    /// <c>custom</c>, and the system then reads ONLY that copy; ResetSystem() drops it. The first id in the
    /// list is the standard overlay (Ctrl+O lands on it first). Old per-overlay tunables are migrated once
    /// (standard overlay's values seed the defaults). Called from <c>Main.BuildSettings</c>.
    /// </summary>
    internal static class OverlaySettingsRegistry
    {
        private static readonly Func<OverlaySystem>[] SystemTypes =
        {
            () => new GridSystem(),
            () => new SpatialSystem(),
            () => new SonarSystem(),
            () => new ElevationSystem(),
            () => new WallToneSystem(),
            () => new FogSystem(),
            () => new ObjectCueSystem(),
            () => new PathInfoSystem(),
            () => new LogSystem(),
        };

        // One prototype per system type (for Key/Name/schema; never bound or ticked).
        private static readonly List<OverlaySystem> Prototypes = BuildPrototypes();
        private static List<OverlaySystem> BuildPrototypes()
        {
            var list = new List<OverlaySystem>();
            foreach (var make in SystemTypes) list.Add(make());
            return list;
        }

        /// <summary>The system keys in declaration order (the order nodes render everywhere).</summary>
        public static IEnumerable<string> SystemKeys()
        {
            foreach (var p in Prototypes) yield return p.Key;
        }

        /// <summary>The system's raw display name, or null if the key isn't a system.</summary>
        public static string SystemName(string key)
        {
            foreach (var p in Prototypes) if (p.Key == key) return p.Name;
            return null;
        }

        private static Func<OverlaySystem> FactoryFor(string key)
        {
            for (int i = 0; i < Prototypes.Count; i++)
                if (Prototypes[i].Key == key) return SystemTypes[i];
            return null;
        }

        private static CategorySetting DefaultsFor(string key)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>(key);

        private static CategorySetting SystemCat(string id, string key)
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<CategorySetting>(id)?.Get<CategorySetting>(key);

        private static readonly Choice[] ModeChoices =
        {
            new Choice("none", "None"),
            new Choice("continuous", "Continuous"),
            new Choice("tiled", "Tiled"),
        };

        // The invisible Default overlay's out-of-box composition (which systems are on). Stored as
        // defaults.<system>.enabled, user-editable from the Sonar/Log/Exploration tabs.
        private static readonly HashSet<string> DefaultOn =
            new HashSet<string> { "grid", "sonar", "fog", "object", "path", "log" };

        private static readonly Dictionary<string, Overlay> _objects = new Dictionary<string, Overlay>();

        /// <summary>Pre-load (in BuildSettings): create only the overlays category + the id list, so Load
        /// can apply the saved list. The overlay subtrees are built afterwards (see <see cref="BuildOverlays"/>),
        /// because we don't know the user's ids until the list has loaded.</summary>
        public static void Register()
        {
            ModSettingsRegistry.EnsureCategory("overlays", "Overlays", "category.overlays");
            EnsureList();
            RegisterDefaults();
        }

        // The SHARED settings (the settings redesign): every system tunable once under
        // defaults.<system> (surfaced by the Sonar / Log / Exploration tabs), and every audio system
        // volume once under audio.volumes (the Audio tab). Overlays hold only composition (enabled +
        // cursor) plus optional whole-subtree overrides.
        private static void RegisterDefaults()
        {
            foreach (var proto in Prototypes)
            {
                var cat = ModSettingsRegistry.EnsureCategory("defaults." + proto.Key,
                    "Defaults/" + proto.Name, "system." + proto.Key);
                if (cat.Children.Count == 0)
                {
                    // The Default overlay's composition lives here too (it has no subtree of its own).
                    // NOT part of RegisterSettings, so per-overlay custom copies don't duplicate it.
                    cat.Add(new BoolSetting("enabled", "Enabled", DefaultOn.Contains(proto.Key), "overlay.enabled"));
                    proto.RegisterSettings(cat);
                }
            }
            // The Default overlay's cursor (mode + speed per slot) — surfaced on the Exploration tab.
            var cursorCat = ModSettingsRegistry.EnsureCategory("defaults.cursor", "Defaults/Cursor", "overlay.cursor");
            if (cursorCat.GetByKey("announce_rooms") == null)
                cursorCat.Add(new BoolSetting("announce_rooms", "Announce room changes", true,
                    "overlay.cursor.announce_rooms"));
            BuildSlotSettings("defaults.cursor.primary", "Defaults/Cursor/Primary", "overlay.cursor.primary", "tiled", 15);
            BuildSlotSettings("defaults.cursor.secondary", "Defaults/Cursor/Secondary", "overlay.cursor.secondary", "none", 15);

            var volumes = ModSettingsRegistry.EnsureCategory("audio.volumes", "Audio/System volumes", "audio.volumes");
            foreach (var proto in Prototypes)
                if (proto is AudioSystem && volumes.GetByKey(proto.Key) == null)
                    volumes.Add(new IntSetting(proto.Key, proto.Name + " volume", 100, 0, 100, 5,
                        "audio.volumes." + proto.Key));
        }

        private static CategorySetting BuildSlotSettings(string path, string labelPath, string locKey,
            string defaultMode, int defaultSpeed)
        {
            var cat = ModSettingsRegistry.EnsureCategory(path, labelPath, locKey);
            if (cat.GetByKey("mode") == null)
                cat.Add(new ChoiceSetting("mode", "Movement mode", ModeChoices, defaultMode, "overlay.mode"));
            if (cat.GetByKey("speed") == null)
                cat.Add(new IntSetting("speed", "Speed (feet/sec)", defaultSpeed, 1, 60, 1, "overlay.speed"));
            return cat;
        }

        /// <summary>Post-load (after ModSettings.Initialize): build every overlay in the now-loaded list,
        /// re-apply their saved values onto the freshly-created subtrees, and publish the live set.</summary>
        public static void BuildOverlays()
        {
            _objects.Clear();
            foreach (var id in Ids()) _objects[id] = BuildOverlayObject(id);
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown();    // applies enabled/cursor/customized flags saved before the subtrees existed
            MaterializeCustomizedSubtrees(); // flagged systems get their custom copies (seeded from defaults)
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown();    // applies the saved custom.* values over the seeds
            MigrateLegacyTunables();         // pre-redesign per-overlay tunables -> shared defaults/volumes
            Publish();
            ModSettings.Save();              // normalize the file (applied keys now persisted as known)
        }

        // ---- whole-subtree customization ----

        public static bool IsCustomized(string id, string sysKey)
            => SystemCat(id, sysKey)?.Get<BoolSetting>("customized")?.Get() == true;

        /// <summary>Give the overlay its own full copy of the system settings, seeded from the
        /// current shared defaults. The system reads the copy from then on (whole-subtree semantics).</summary>
        public static void Customize(string id, string sysKey)
        {
            var sCat = SystemCat(id, sysKey);
            if (sCat == null || sCat.Get<CategorySetting>("custom") != null) return;
            CreateCustomTree(sCat, sysKey);
            sCat.Get<BoolSetting>("customized")?.Set(true);
            ModSettings.Reindex();
            ModSettings.MarkDirty();
        }

        /// <summary>Drop the overlay copy — the system follows the shared defaults again.</summary>
        public static void ResetSystem(string id, string sysKey)
        {
            var sCat = SystemCat(id, sysKey);
            if (sCat == null) return;
            var custom = sCat.Get<CategorySetting>("custom");
            if (custom != null) sCat.Remove(custom);
            sCat.Get<BoolSetting>("customized")?.Set(false);
            ModSettings.Reindex();
            ModSettings.MarkDirty();
        }

        private static void MaterializeCustomizedSubtrees()
        {
            foreach (var id in Ids())
                foreach (var proto in Prototypes)
                {
                    var sCat = SystemCat(id, proto.Key);
                    if (sCat != null && sCat.Get<BoolSetting>("customized")?.Get() == true
                        && sCat.Get<CategorySetting>("custom") == null)
                        CreateCustomTree(sCat, proto.Key);
                }
        }

        private static void CreateCustomTree(CategorySetting sysCat, string key)
        {
            var custom = new CategorySetting("custom", "Custom settings", localizationKey: "overlay.custom");
            sysCat.Add(custom);
            FactoryFor(key)?.Invoke().RegisterSettings(custom); // the SAME schema as the defaults tree
            var defaults = DefaultsFor(key);
            if (defaults != null) CopyValues(defaults, custom);
        }

        private static void CopyValues(CategorySetting from, CategorySetting to)
        {
            foreach (var src in from.Children)
            {
                var dst = to.GetByKey(src.Key);
                if (dst == null) continue;
                if (src is CategorySetting fc && dst is CategorySetting tc) CopyValues(fc, tc);
                else if (!(src is CategorySetting) && !(dst is CategorySetting))
                {
                    var v = src.BoxedValue;
                    if (v != null) { try { dst.LoadValue(v); } catch { } }
                }
            }
        }

        // One-shot: settings saved before the redesign hold tunables at overlays.<id>.<sys>.<key>.
        // Seed the shared defaults (and audio volumes) from the STANDARD overlay values, then purge
        // every overlay legacy tunable key (non-standard divergence is discarded by design).
        private static void MigrateLegacyTunables()
        {
            var ids = Ids();
            if (ids.Count == 0) return;
            var std = ids[0];
            foreach (var proto in Prototypes)
            {
                string prefix = "overlays." + std + "." + proto.Key + ".";
                foreach (var path in ModSettings.UnknownPaths())
                {
                    if (!path.StartsWith(prefix)) continue;
                    var rest = path.Substring(prefix.Length);
                    if (rest == "enabled" || rest == "customized" || rest.StartsWith("custom.")) continue;
                    string target = rest == "volume"
                        ? "audio.volumes." + proto.Key
                        : "defaults." + proto.Key + "." + rest;
                    var setting = ModSettings.GetSetting<Setting>(target);
                    if (setting != null && ModSettings.TryGetUnknown(path, out var tok))
                    {
                        try { setting.LoadValue(tok); } catch { }
                    }
                }
            }
            foreach (var id in ids)
                foreach (var proto in Prototypes)
                {
                    string prefix = "overlays." + id + "." + proto.Key + ".";
                    ModSettings.RemoveUnknownWhere(p =>
                        p.StartsWith(prefix) && !p.EndsWith(".enabled") && !p.Contains(".custom"));
                }
        }

        // ---- runtime add / remove ----

        /// <summary>Append a new overlay (generic config) and make it live. Returns its id.</summary>
        public static string Add()
        {
            var ids = Ids();
            var id = NewId(ids);
            // Create the name first (so the object reads it), then build the subtree + object.
            var oCat = ModSettingsRegistry.EnsureCategory("overlays." + id, "Overlays/Overlay");
            if (oCat.Get<StringSetting>("name") == null)
                oCat.Add(new StringSetting("name", "Name", "Overlay " + (ids.Count + 1), "overlay.name") { Hidden = true });

            ids.Add(id);
            ListSetting().Set(string.Join(",", ids));
            _objects[id] = BuildOverlayObject(id);
            ModSettings.Reindex();
            Publish();
            ModSettings.MarkDirty();
            return id;
        }

        /// <summary>The user-added overlay ids in cycle order (the invisible Default precedes them all).</summary>
        public static IReadOnlyList<string> OverlayIds() => Ids();

        /// <summary>The live display name of an overlay (its hidden name setting), falling back to the id.</summary>
        public static string OverlayName(string id)
            => NameSetting(id)?.Get() ?? id;

        /// <summary>Rename an overlay (persists, and updates the live object's spoken name).</summary>
        public static void SetOverlayName(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            NameSetting(id)?.Set(name); // StringSetting.Set auto-saves
            if (_objects.TryGetValue(id, out var o)) o.Name = name;
        }

        private static StringSetting NameSetting(string id)
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<CategorySetting>(id)?.Get<StringSetting>("name");

        /// <summary>Remove an overlay (the invisible Default always remains). Returns true if removed.</summary>
        public static bool Remove(string id)
        {
            var ids = Ids();
            if (!ids.Contains(id)) return false;
            ids.Remove(id);
            ListSetting().Set(string.Join(",", ids));

            var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
            var sub = overlays?.GetByKey(id);
            if (sub != null) overlays.Remove(sub);
            _objects.Remove(id);

            ModSettings.Reindex();
            Publish();
            ModSettings.MarkDirty();
            return true;
        }

        // ---- building ----

        private static Overlay BuildOverlayObject(string id)
        {
            var oCat = ModSettingsRegistry.EnsureCategory("overlays." + id, "Overlays/Overlay");

            var nameSetting = oCat.Get<StringSetting>("name");
            if (nameSetting == null)
            {
                nameSetting = new StringSetting("name", "Name", "Overlay", "overlay.name") { Hidden = true };
                oCat.Add(nameSetting);
            }
            oCat.LabelProvider = () => nameSetting.Get(); // the menu node reads the live name

            var overlay = new Overlay(nameSetting.Get());

            foreach (var make in SystemTypes)
            {
                var sys = make();
                var sCat = ModSettingsRegistry.EnsureCategory("overlays." + id + "." + sys.Key,
                    "Overlays/" + nameSetting.Get() + "/" + sys.Name);
                // Composition only: enabled + the customized flag. Tunables live in the shared
                // defaults, or in a custom subtree materialized by Customize().
                if (sCat.GetByKey("enabled") == null) // new overlays start as a copy of the Default
                    sCat.Add(new BoolSetting("enabled", "Enabled",
                        DefaultsFor(sys.Key)?.Get<BoolSetting>("enabled")?.Get() ?? DefaultOn.Contains(sys.Key),
                        "overlay.enabled"));
                if (sCat.GetByKey("customized") == null)
                    sCat.Add(new BoolSetting("customized", "Customized", false) { Hidden = true });
                sys.Bind(sCat, DefaultsFor(sys.Key));
                overlay.With(sys);
            }

            var primaryCat = BuildSlot(id, "primary", "Primary", DefaultSlotMode("primary", "tiled"));
            var secondaryCat = BuildSlot(id, "secondary", "Secondary", DefaultSlotMode("secondary", "none"));
            overlay.Cursor.SetSlots(primaryCat, secondaryCat);
            WireModeChange(primaryCat, overlay);
            WireModeChange(secondaryCat, overlay);
            return overlay;
        }

        private static CategorySetting BuildSlot(string id, string key, string label, string defaultMode)
        {
            var cat = ModSettingsRegistry.EnsureCategory("overlays." + id + ".cursor." + key,
                "Overlays/_/Cursor/" + label); // overlay name segment is irrelevant (already labelled)
            if (cat.GetByKey("mode") == null)
                cat.Add(new ChoiceSetting("mode", "Movement mode", ModeChoices, defaultMode, "overlay.mode"));
            if (cat.GetByKey("speed") == null)
                cat.Add(new IntSetting("speed", "Speed (feet/sec)", 15, 1, 60, 1, "overlay.speed"));
            return cat;
        }

        private static void WireModeChange(CategorySetting slotCat, Overlay overlay)
        {
            var mode = slotCat?.Get<ChoiceSetting>("mode");
            if (mode != null) mode.Changed += _ => overlay.Cursor.ResolveModes();
        }

        // The invisible Default overlay: always first in the Ctrl+O cycle, never in the Overlays tab.
        // Its tunables AND composition are the shared defaults themselves — systems bind the defaults
        // category as their per-overlay category too, so `enabled` resolves to defaults.<sys>.enabled.
        internal static Overlay DefaultOverlay => _defaultOverlay;

        private static Overlay _defaultOverlay;

        private static Overlay BuildDefaultOverlay()
        {
            var overlay = new Overlay(Message.Localized("ui", "overlay.default_name").Resolve());
            foreach (var make in SystemTypes)
            {
                var sys = make();
                var d = DefaultsFor(sys.Key);
                sys.Bind(d, d);
                overlay.With(sys);
            }
            var primary = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")?.Get<CategorySetting>("primary");
            var secondary = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")?.Get<CategorySetting>("secondary");
            overlay.Cursor.SetSlots(primary, secondary);
            WireModeChange(primary, overlay);
            WireModeChange(secondary, overlay);
            return overlay;
        }

        private static void Publish()
        {
            if (_defaultOverlay == null) _defaultOverlay = BuildDefaultOverlay();
            var list = new List<Overlay> { _defaultOverlay };
            list.AddRange(Ids().Select(id => _objects.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null));
            OverlayManager.SetOverlays(list);
        }

        // ---- list helpers ----

        private static string DefaultSlotMode(string slot, string fallback)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")
                ?.Get<CategorySetting>(slot)?.Get<ChoiceSetting>("mode")?.Current?.Id ?? fallback;

        // The list starts EMPTY: the invisible Default overlay is always present, and users add
        // explicit overlays only when they want a deviating lens.
        private static string DefaultList => "";

        private static void EnsureList()
        {
            var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
            if (overlays != null && overlays.Get<StringSetting>("list") == null)
                overlays.Add(new StringSetting("list", "Overlay list", DefaultList, "overlay.list") { Hidden = true });
        }

        private static StringSetting ListSetting()
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<StringSetting>("list");

        private static List<string> Ids()
        {
            var raw = ListSetting()?.Get() ?? DefaultList;
            return raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        }

        private static string NewId(List<string> ids)
        {
            int n = 1;
            while (ids.Contains("overlay_" + n) || _objects.ContainsKey("overlay_" + n)) n++;
            return "overlay_" + n;
        }

    }
}
