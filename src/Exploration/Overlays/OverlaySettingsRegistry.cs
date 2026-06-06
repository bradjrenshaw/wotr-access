using System;
using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Builds the data-driven overlays from settings and supports adding/removing them at runtime. The set
    /// of overlays is a persisted id list (<c>overlays.list</c>); each id has a settings subtree
    /// (<c>overlays.&lt;id&gt;</c>): a hidden display name, a cursor (per-slot movement-mode choice + speed),
    /// and every system (each enable-gated) with its tunables. The first id in the list is the standard
    /// overlay (what Ctrl+O lands on first). Two seeds ship by default and reproduce the old behavior; new
    /// overlays start from a sensible generic config. Systems read their settings live, so edits apply
    /// immediately; add/remove rebuild the live set and re-save. Called from <c>Main.BuildSettings</c>.
    /// </summary>
    internal static class OverlaySettingsRegistry
    {
        private static readonly Func<OverlaySystem>[] SystemTypes =
        {
            () => new GridSystem(),
            () => new SpatialSystem(),
            () => new SonarSystem(),
            () => new WallToneSystem(),
            () => new FogSystem(),
            () => new ObjectCueSystem(),
        };

        private static readonly Choice[] ModeChoices =
        {
            new Choice("none", "None"),
            new Choice("continuous", "Continuous"),
            new Choice("tiled", "Tiled"),
        };

        private sealed class Seed
        {
            public readonly string Id, Name, Primary, Secondary;
            public readonly HashSet<string> Enabled;
            public Seed(string id, string name, string primary, string secondary, params string[] enabled)
            { Id = id; Name = name; Primary = primary; Secondary = secondary; Enabled = new HashSet<string>(enabled); }
        }

        private static readonly Seed[] Seeds =
        {
            new Seed("tile_view", "Tile view", "tiled", "none", "grid", "sonar", "fog", "object"),
            new Seed("continuous", "Continuous mode", "continuous", "none", "spatial", "sonar", "walltones", "fog", "object"),
        };

        // A new (non-seed) overlay's starting config: a tile cursor + the speech/cue systems.
        private static readonly HashSet<string> GenericEnabled = new HashSet<string> { "grid", "sonar", "fog", "object" };

        private static readonly Dictionary<string, Overlay> _objects = new Dictionary<string, Overlay>();

        /// <summary>Pre-load (in BuildSettings): create only the overlays category + the id list, so Load
        /// can apply the saved list. The overlay subtrees are built afterwards (see <see cref="BuildOverlays"/>),
        /// because we don't know the user's ids until the list has loaded.</summary>
        public static void Register()
        {
            ModSettingsRegistry.EnsureCategory("overlays", "Overlays", "category.overlays");
            EnsureList();
        }

        /// <summary>Post-load (after ModSettings.Initialize): build every overlay in the now-loaded list,
        /// re-apply their saved values onto the freshly-created subtrees, and publish the live set.</summary>
        public static void BuildOverlays()
        {
            _objects.Clear();
            foreach (var id in Ids()) _objects[id] = BuildOverlayObject(id);
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown(); // saved overlay values were "unknown" at Load time (subtrees didn't exist)
            Publish();
            ModSettings.Save();           // normalize the file (applied keys now persisted as known)
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

        /// <summary>The overlay ids in order; the first is the standard one (Ctrl+O lands on it first).</summary>
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

        /// <summary>Whether this overlay is the standard one (first in the list).</summary>
        public static bool IsStandard(string id)
        {
            var ids = Ids();
            return ids.Count > 0 && ids[0] == id;
        }

        /// <summary>Make an overlay the standard one by moving it to the front of the list.</summary>
        public static bool MakeDefault(string id)
        {
            var ids = Ids();
            if (!ids.Contains(id) || ids[0] == id) return false;
            ids.Remove(id);
            ids.Insert(0, id);
            ListSetting().Set(string.Join(",", ids));
            Publish();
            ModSettings.MarkDirty();
            return true;
        }

        /// <summary>Remove an overlay (keeps at least one). Returns true if removed.</summary>
        public static bool Remove(string id)
        {
            var ids = Ids();
            if (ids.Count <= 1 || !ids.Contains(id)) return false;
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
            var seed = SeedFor(id);
            var oCat = ModSettingsRegistry.EnsureCategory("overlays." + id, "Overlays/" + (seed?.Name ?? "Overlay"));

            var nameSetting = oCat.Get<StringSetting>("name");
            if (nameSetting == null)
            {
                nameSetting = new StringSetting("name", "Name", seed?.Name ?? "Overlay", "overlay.name") { Hidden = true };
                oCat.Add(nameSetting);
            }
            oCat.LabelProvider = () => nameSetting.Get(); // the menu node reads the live name

            var overlay = new Overlay(nameSetting.Get());

            foreach (var make in SystemTypes)
            {
                var sys = make();
                var sCat = ModSettingsRegistry.EnsureCategory("overlays." + id + "." + sys.Key,
                    "Overlays/" + nameSetting.Get() + "/" + sys.Name);
                if (sCat.GetByKey("enabled") == null) // fresh subtree → create its settings once
                {
                    sCat.Add(new BoolSetting("enabled", "Enabled", DefaultEnabled(seed, sys.Key), "overlay.enabled"));
                    sys.RegisterSettings(sCat);
                }
                sys.Bind(sCat);
                overlay.With(sys);
            }

            var primaryCat = BuildSlot(id, "primary", "Primary", seed?.Primary ?? "tiled");
            var secondaryCat = BuildSlot(id, "secondary", "Secondary", seed?.Secondary ?? "none");
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

        private static void Publish()
            => OverlayManager.SetOverlays(Ids().Select(id => _objects.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null).ToList());

        // ---- list helpers ----

        private static string DefaultList => string.Join(",", Seeds.Select(s => s.Id));

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

        private static Seed SeedFor(string id) => Seeds.FirstOrDefault(s => s.Id == id);

        private static bool DefaultEnabled(Seed seed, string key)
            => (seed?.Enabled ?? GenericEnabled).Contains(key);
    }
}
