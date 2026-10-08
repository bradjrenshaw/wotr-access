using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The CURSOR's settings tree (the cursor refactor, step one — 2026-10-04). The cursor is its own
    /// top-level category, separate from the lenses (overlay systems) that merely read its position:
    /// <code>
    /// cursor
    ///   exploration      behaviours (announce rooms, wall slide, …) + movement
    ///   worldmap         movement
    ///   battles          movement (reserved for crusade tactical combat; inert until that is built)
    ///     movement
    ///       primary | secondary | tertiary | quaternary      (W A S D / Shift+W A S D / arrows / Shift+arrows)
    ///         mode: none | continuous | tiled
    ///         continuous: speed            shown only while mode == continuous
    ///         tiled: tile size             shown only while mode == tiled (feet in an area, miles on the map)
    /// </code>
    /// Each mode keeps its OWN subtree so switching modes swaps what is shown without losing the other
    /// mode's tuned values (the subtree not matching the dropdown is hidden, never overwritten). An
    /// overlay can customize its cursor the way it customizes a system: a full copy of this schema
    /// under <c>overlays.&lt;id&gt;.cursor.custom</c> (see <see cref="OverlaySettingsRegistry"/>), read
    /// instead of the defaults while that overlay is engaged. Readers go through <see cref="Context"/>,
    /// which resolves that choice for them.
    /// </summary>
    internal static class CursorSettings
    {
        public const string Exploration = "exploration";
        public const string WorldMap = "worldmap";
        public const string Battles = "battles";
        public static readonly string[] Contexts = { Exploration, WorldMap, Battles };

        public static readonly string[] SlotKeys = { "primary", "secondary", "tertiary", "quaternary" };

        public const string ModeNone = "none";
        public const string ModeContinuous = "continuous";
        public const string ModeTiled = "tiled";

        private static readonly Choice[] AllModes =
        {
            new Choice(ModeNone, "None", "choice.mode.none"),
            new Choice(ModeContinuous, "Continuous", "choice.mode.continuous"),
            new Choice(ModeTiled, "Tiled", "choice.mode.tiled"),
        };
        private static readonly Choice[] SteppingModes = { AllModes[0], AllModes[2] }; // battles: no free glide on a hex grid

        /// <summary>Fired after any slot's mode changes, in the defaults or in an overlay's copy — the
        /// cursors rebuild their movement modes from it.</summary>
        public static event Action Changed;

        /// <summary>The shared defaults root (<c>cursor</c>).</summary>
        public static CategorySetting Root => ModSettings.Root.Get<CategorySetting>("cursor");

        /// <summary>Pre-load: create the defaults tree so saved values can apply onto it.</summary>
        public static void Register()
        {
            var root = ModSettingsRegistry.EnsureCategory("cursor", "Cursor", "category.cursor");
            RegisterTree(root);
        }

        /// <summary>The whole schema under a root — the defaults, or an overlay's custom copy (the SAME
        /// schema, so values copy across by key).</summary>
        public static void RegisterTree(CategorySetting root)
        {
            // Exploration: the in-area cursor's behaviours, then its movement.
            var ex = Ensure(root, Exploration, "Exploration", "cursor.exploration");
            AddBool(ex, "announce_rooms", "Announce room changes", true, "overlay.cursor.announce_rooms");
            AddBool(ex, "wall_slide", "Slide along walls", false, "overlay.cursor.wall_slide");
            AddBool(ex, "direction_priority", "First-held direction has priority", false, "overlay.cursor.direction_priority");
            AddBool(ex, "review_reset", "Cursor movement resets review cycles", false, "overlay.cursor.review_reset");
            AddBool(ex, "collision_names", "Name blocking objects on collision", false, "overlay.cursor.collision_names");
            TerrainSounds.RegisterSettings(ex);
            var exm = Ensure(ex, "movement", "Movement modes", "cursor.movement");
            Slot(exm, Exploration, "primary", ModeContinuous, 15);
            Slot(exm, Exploration, "secondary", ModeContinuous, 30);
            Slot(exm, Exploration, "tertiary", ModeTiled, 15);
            Slot(exm, Exploration, "quaternary", ModeNone, 15);

            // World map: units are miles (the global map equates one world unit with one mile).
            var wm = Ensure(root, WorldMap, "World map", "cursor.worldmap");
            var wmm = Ensure(wm, "movement", "Movement modes", "cursor.movement");
            Slot(wmm, WorldMap, "primary", ModeContinuous, 18);
            Slot(wmm, WorldMap, "secondary", ModeContinuous, 45);
            Slot(wmm, WorldMap, "tertiary", ModeTiled, 18);
            Slot(wmm, WorldMap, "quaternary", ModeNone, 18);

            // Crusade battles: reserved. The hex grid wants stepping; a slot is none or tiled.
            var bt = Ensure(root, Battles, "Crusade battles", "cursor.battles");
            var btm = Ensure(bt, "movement", "Movement modes", "cursor.movement");
            Slot(btm, Battles, "primary", ModeTiled, 0);
            Slot(btm, Battles, "secondary", ModeNone, 0);
            Slot(btm, Battles, "tertiary", ModeTiled, 0);
            Slot(btm, Battles, "quaternary", ModeNone, 0);
        }

        // One input slot: the mode dropdown plus a subtree per mode this context offers. The dropdown
        // hides the subtrees that don't match (visibility, not values) and raises Changed. The subtrees
        // render INLINE — the selected mode's settings read right after the dropdown, no sub-heading.
        private static void Slot(CategorySetting movement, string ctx, string key, string defaultMode, int defaultSpeed)
        {
            var slot = Ensure(movement, key, SlotLabel(key), "cursor.slot." + key);
            var choices = ctx == Battles ? SteppingModes : AllModes;
            var mode = slot.Get<ChoiceSetting>("mode");
            if (mode == null)
            {
                mode = new ChoiceSetting("mode", "Movement mode", choices, defaultMode, "overlay.movement_mode");
                slot.Add(mode);
                mode.Changed += _ => { ApplyVisibility(slot); Changed?.Invoke(); };
            }
            if (ctx != Battles)
            {
                var cont = Ensure(slot, ModeContinuous, "Continuous", "cursor.continuous");
                cont.Inline = true; // its settings sit right under the mode dropdown
                if (cont.GetByKey("speed") == null)
                    cont.Add(ctx == WorldMap
                        ? new IntSetting("speed", "Speed (miles per second)", defaultSpeed, 1, 100, 1, "cursor.speed_miles")
                        : new IntSetting("speed", "Speed (feet per second)", defaultSpeed, 1, 60, 1, "cursor.speed_feet"));
            }
            var tiled = Ensure(slot, ModeTiled, "Tiled", "cursor.tiled");
            tiled.Inline = true;
            if (tiled.GetByKey("cell_size") == null)
            {
                if (ctx == WorldMap) tiled.Add(new IntSetting("cell_size", "Tile size (miles)", 2, 1, 50, 1, "cursor.cell_miles"));
                else if (ctx == Exploration) tiled.Add(new IntSetting("cell_size", "Tile size (feet)", 5, 1, 30, 1, "cursor.cell_feet"));
                // (Battles: the game's hex grid fixes the step; the subtree stays empty and renders nothing.)
            }
            ApplyVisibility(slot);
        }

        private static string SlotLabel(string key)
        {
            switch (key)
            {
                case "primary": return "Primary (W A S D)";
                case "secondary": return "Secondary (Shift and W A S D)";
                case "tertiary": return "Tertiary (arrow keys)";
                default: return "Quaternary (Shift and arrow keys)";
            }
        }

        private static void ApplyVisibility(CategorySetting slot)
        {
            var id = slot.Get<ChoiceSetting>("mode")?.Current?.Id ?? ModeNone;
            var cont = slot.Get<CategorySetting>(ModeContinuous);
            if (cont != null) cont.Hidden = id != ModeContinuous;
            var tiled = slot.Get<CategorySetting>(ModeTiled);
            if (tiled != null) tiled.Hidden = id != ModeTiled;
        }

        /// <summary>Re-derive every slot's shown subtree from its loaded mode — after values load, when
        /// the dropdown's change event hasn't run for them.</summary>
        public static void RefreshVisibility(CategorySetting root)
        {
            if (root == null) return;
            foreach (var ctx in Contexts)
            {
                var movement = root.Get<CategorySetting>(ctx)?.Get<CategorySetting>("movement");
                if (movement == null) continue;
                foreach (var key in SlotKeys)
                {
                    var slot = movement.Get<CategorySetting>(key);
                    if (slot != null) ApplyVisibility(slot);
                }
            }
        }

        // ---- resolution (what a reader sees) ----

        /// <summary>The effective settings for a context: the given (else the engaged) overlay's custom
        /// cursor copy when it has one, else the shared defaults.</summary>
        public static CategorySetting Context(string ctx, Overlay overlay = null)
        {
            var o = overlay ?? OverlayManager.ActiveOverlay;
            var root = o?.CursorRoot ?? Root;
            return root?.Get<CategorySetting>(ctx);
        }

        public static CategorySetting Slot(CategorySetting ctx, MovementSlot slot)
            => ctx?.Get<CategorySetting>("movement")?.Get<CategorySetting>(SlotKeys[(int)slot]);

        public static string Mode(CategorySetting slotCat)
            => slotCat?.Get<ChoiceSetting>("mode")?.Current?.Id ?? ModeNone;

        public static int ContinuousSpeed(CategorySetting slotCat, int fallback)
            => slotCat?.Get<CategorySetting>(ModeContinuous)?.Get<IntSetting>("speed")?.Get() ?? fallback;

        public static int TiledCell(CategorySetting slotCat, int fallback)
            => slotCat?.Get<CategorySetting>(ModeTiled)?.Get<IntSetting>("cell_size")?.Get() ?? fallback;

        /// <summary>An in-area tiled slot's cell edge in world metres (the setting is in feet).</summary>
        public static float TiledCellMetres(CategorySetting slotCat)
            => TiledCell(slotCat, 5) * Geo.MetresPerFoot;

        /// <summary>A tiled slot's cell edge in a space's world units (feet → metres in an area; miles on the map).</summary>
        public static float TiledCellWorld(CategorySetting slotCat, CursorSpace space)
            => TiledCell(slotCat, space.DefaultCell) * space.Unit;

        /// <summary>The tile size a reader should assume when no slot has stepped yet: the first tiled
        /// slot's, else the space's default. Seeds <see cref="Overlays.Cursor.TileCell"/>.</summary>
        public static float DefaultTileCell(CategorySetting ctx, CursorSpace space)
        {
            foreach (var slot in CursorKeys.Slots)
            {
                var sc = Slot(ctx, slot);
                if (Mode(sc) == ModeTiled) return TiledCellWorld(sc, space);
            }
            return space.DefaultCell * space.Unit;
        }

        public static bool Flag(CategorySetting ctx, string key, bool fallback = false)
            => ctx?.Get<BoolSetting>(key)?.Get() ?? fallback;

        // ---- migration from the pre-refactor layout ----

        /// <summary>One-shot, post-load: carry the old keys (<c>defaults.cursor.*</c> with its two
        /// slots' in-area and world-map mode/speed, the grid's world-map tile size) into the new tree,
        /// then drop them along with the old per-overlay slot copies.</summary>
        public static void MigrateLegacy()
        {
            var root = Root;
            if (root == null) return;
            var map = new Dictionary<string, string[]>();
            foreach (var flag in new[] { "announce_rooms", "wall_slide", "direction_priority", "review_reset", "collision_names", "terrain_sounds", "terrain_interval" })
                map["defaults.cursor." + flag] = new[] { "cursor.exploration." + flag };
            foreach (var slot in new[] { "primary", "secondary" })
            {
                map["defaults.cursor." + slot + ".mode"] = new[] { "cursor.exploration.movement." + slot + ".mode" };
                map["defaults.cursor." + slot + ".speed"] = new[] { "cursor.exploration.movement." + slot + ".continuous.speed" };
                map["defaults.cursor." + slot + ".worldmap_mode"] = new[] { "cursor.worldmap.movement." + slot + ".mode" };
                map["defaults.cursor." + slot + ".worldmap_speed"] = new[] { "cursor.worldmap.movement." + slot + ".continuous.speed" };
            }
            var cells = new List<string>();
            foreach (var slot in SlotKeys) cells.Add("cursor.worldmap.movement." + slot + ".tiled.cell_size");
            map["defaults.grid.worldmap_cell_size"] = cells.ToArray();
            // The grid system no longer owns the in-area tile size (2026-10-04): its saved value seeds
            // every in-area tiled slot, in the defaults and in each overlay whose cursor copy exists (an
            // overlay that customized only its grid has no cursor copy to carry a per-overlay size; it
            // follows the defaults, which got the same value).
            cells = new List<string>();
            foreach (var slot in SlotKeys) cells.Add("cursor.exploration.movement." + slot + ".tiled.cell_size");
            map["defaults.grid.cell_size"] = cells.ToArray();
            foreach (var path in ModSettings.UnknownPaths())
            {
                var m = System.Text.RegularExpressions.Regex.Match(path, @"^overlays\.([^.]+)\.grid\.custom\.cell_size$");
                if (!m.Success) continue;
                cells = new List<string>();
                foreach (var slot in SlotKeys)
                    cells.Add("overlays." + m.Groups[1].Value + ".cursor.custom.exploration.movement." + slot + ".tiled.cell_size");
                map[path] = cells.ToArray();
            }

            int moved = 0;
            foreach (var pair in map)
            {
                if (!ModSettings.TryGetUnknown(pair.Key, out var tok)) continue;
                foreach (var target in pair.Value)
                {
                    var s = ModSettings.GetSetting<Setting>(target);
                    if (s == null) continue;
                    try { s.LoadValue(tok); moved++; } catch { }
                }
            }
            ModSettings.RemoveUnknownWhere(p => p.StartsWith("defaults.cursor.") || p == "defaults.grid.worldmap_cell_size"
                || p == "defaults.grid.cell_size" || p.EndsWith(".grid.custom.cell_size")
                || (p.StartsWith("overlays.") && (p.Contains(".cursor.primary.") || p.Contains(".cursor.secondary."))));
            if (moved > 0) Main.Log?.Log("[cursor] migrated " + moved + " pre-refactor cursor settings into the cursor tree");
            RefreshVisibility(root);
        }

        // ---- helpers ----

        private static CategorySetting Ensure(CategorySetting parent, string key, string label, string loc)
        {
            var cat = parent.Get<CategorySetting>(key);
            if (cat == null)
            {
                cat = new CategorySetting(key, label, localizationKey: loc);
                parent.Add(cat);
            }
            return cat;
        }

        private static void AddBool(CategorySetting cat, string key, string label, bool def, string loc)
        {
            if (cat.GetByKey(key) == null) cat.Add(new BoolSetting(key, label, def, loc));
        }
    }
}
