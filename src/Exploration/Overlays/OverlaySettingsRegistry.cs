using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Builds the data-driven overlays from settings. Every overlay carries every system (each enable-gated)
    /// plus a cursor with per-slot movement-mode choices; an overlay is therefore just a settings subtree
    /// under <c>overlays.&lt;id&gt;</c>, and "composing" one is flipping enables / picking modes. Seeds the
    /// two built-in overlays on first run (their defaults reproduce the old behavior); the systems read
    /// their settings live, so edits take effect immediately. Called from <c>Main.BuildSettings</c> before
    /// <c>ModSettings.Initialize</c>. (Add / remove / rename of overlays + the default choice come next.)
    /// </summary>
    internal static class OverlaySettingsRegistry
    {
        // One fresh system instance per overlay; the instance's Key names its settings category.
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

        public static void Register()
        {
            ModSettingsRegistry.EnsureCategory("overlays", "Overlays", "category.overlays");
            var built = new List<Overlay>();
            foreach (var seed in Seeds) built.Add(BuildOverlay(seed));
            OverlayManager.SetOverlays(built);
        }

        private static Overlay BuildOverlay(Seed seed)
        {
            var basePath = "overlays." + seed.Id;
            ModSettingsRegistry.EnsureCategory(basePath, "Overlays/" + seed.Name);
            var overlay = new Overlay(seed.Name);

            // Cursor: primary + secondary slots (movement mode + speed).
            var primaryCat = BuildSlot(basePath, seed.Name, "primary", "Primary", seed.Primary);
            var secondaryCat = BuildSlot(basePath, seed.Name, "secondary", "Secondary", seed.Secondary);

            // Systems: every type present, enable-gated, each wired to its own settings category.
            foreach (var make in SystemTypes)
            {
                var sys = make();
                var sCat = ModSettingsRegistry.EnsureCategory(basePath + "." + sys.Key, "Overlays/" + seed.Name + "/" + sys.Name);
                if (sCat.GetByKey("enabled") == null)
                    sCat.Add(new BoolSetting("enabled", "Enabled", seed.Enabled.Contains(sys.Key), "overlay.enabled"));
                sys.RegisterSettings(sCat);
                sys.Bind(sCat);
                overlay.With(sys);
            }

            // Movement modes come from the cursor slot choices; re-resolve live when they change.
            overlay.Cursor.SetSlots(primaryCat, secondaryCat);
            WireModeChange(primaryCat, overlay);
            WireModeChange(secondaryCat, overlay);
            return overlay;
        }

        private static void WireModeChange(CategorySetting slotCat, Overlay overlay)
        {
            var mode = slotCat?.Get<ChoiceSetting>("mode");
            if (mode != null) mode.Changed += _ => overlay.Cursor.ResolveModes();
        }

        private static CategorySetting BuildSlot(string basePath, string overlayName, string key, string label, string defaultMode)
        {
            var cat = ModSettingsRegistry.EnsureCategory(basePath + ".cursor." + key, "Overlays/" + overlayName + "/Cursor/" + label);
            if (cat.GetByKey("mode") == null)
                cat.Add(new ChoiceSetting("mode", "Movement mode", ModeChoices, defaultMode, "overlay.mode"));
            if (cat.GetByKey("speed") == null)
                cat.Add(new IntSetting("speed", "Speed (feet/sec)", 15, 1, 60, 1, "overlay.speed"));
            return cat;
        }
    }
}
