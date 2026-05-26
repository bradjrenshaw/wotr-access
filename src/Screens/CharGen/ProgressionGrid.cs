using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.ChupaChupses;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Level;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Spellbook;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Maps the game's progression grid (<see cref="UnitProgressionVM"/>) onto one navigable
    /// <see cref="Table"/>. The game stacks several mini-grids per class — Statistics (BAB/saves), the
    /// spellbook, then one block per <see cref="ProgressionVM"/> (the main progression and each
    /// sub-progression such as Weapon Training), then Feats and any Shared tracks — each with its own
    /// level ruler. We mirror that as groups inside a single grid: every block emits a header row
    /// ("[group name] | Level 1 | Level 2 | …") followed by its data rows, and the navigator resolves a
    /// cell's group/level/row headers by nearest-cell lookup. A feature cell is the feature at that
    /// (line, level) with rank + Added/Removed marker and a Space drill-in; a stat cell is the value at
    /// that level. The game packs rank-less features onto shared lines, which we label "Features".
    /// </summary>
    internal static class ProgressionGrid
    {
        // Each ClassProgressionVM keeps its class/archetype private; we reflect them to compute the
        // per-level BAB/saves (the grid's stat chupachups only flag increases, not the numbers).
        private static readonly System.Reflection.FieldInfo ClassField =
            AccessTools.Field(typeof(ClassProgressionVM), "m_UnitClass");
        private static readonly System.Reflection.FieldInfo ArchetypeField =
            AccessTools.Field(typeof(ClassProgressionVM), "m_UnitArchetype");

        public static Table Build(UnitProgressionVM prog, BlueprintCharacterClass selectedClass)
        {
            var levels = prog?.LevelProgressionVM?.EntryVms;
            if (levels == null || levels.Count == 0) return null;

            // UnitProgressionVM.ClassProgressionVms retains the *previously* selected class as a stale
            // second band (it holds the last two classes touched, newest first), even though the preview
            // unit itself has only the current class. Render only the band for the selected class; fall
            // back to the first (newest) band if we can't match by blueprint.
            var bands = new List<ClassProgressionVM>(SelectBands(prog.ClassProgressionVms, selectedClass));

            var table = new Table("Progression");
            bool prefix = bands.Count > 1; // multiclass → disambiguate group names by class
            foreach (var cls in bands)
                if (cls != null) AddClassBlocks(table, cls, levels, prefix ? cls.Name + " — " : "");

            if (prog.FeatProgressionVM != null)
                AddBand(table, "Feats", prog.FeatProgressionVM.MainChupaChupsLines,
                    prog.FeatProgressionVM.AdditionalChupaChupsList, levels);

            if (prog.SharedProgressionVms != null)
                foreach (var sh in prog.SharedProgressionVms)
                    if (sh != null) AddBand(table, sh.ProgressionName ?? "Shared",
                        sh.MainChupaChupsLines, sh.AdditionalChupaChupsList, levels);

            return table.RowCount > 0 ? table : null;
        }

        // The class band(s) to actually render: the one whose class blueprint matches the current
        // selection. If none match (unexpected), fall back to the first band — it's the newest, since
        // the VM lists newest-first. Empty list if there's nothing to show.
        private static IEnumerable<ClassProgressionVM> SelectBands(
            IList<ClassProgressionVM> all, BlueprintCharacterClass selectedClass)
        {
            if (all == null || all.Count == 0) yield break;
            if (selectedClass != null)
            {
                bool matched = false;
                foreach (var cls in all)
                    if (cls != null && ClassField?.GetValue(cls) as BlueprintCharacterClass == selectedClass)
                    {
                        matched = true;
                        yield return cls;
                    }
                if (matched) yield break;
            }
            yield return all[0]; // fallback: newest band
        }

        // A class's stacked blocks, each its own group: Statistics, Spells (casters), then one block per
        // ProgressionVM (the main progression and any sub-progressions like Weapon Training).
        private static void AddClassBlocks(Table table, ClassProgressionVM cls,
            IList<LevelProgressionEntryVM> levels, string prefix)
        {
            // Class lead-in (the game's class header): name + current level + HP gained per level, as a
            // single group cell at the top of this class's section (no level ruler of its own).
            var head = cls.Name ?? "Class";
            if (cls.Level != null) head += ", level " + cls.Level.Value;
            if (!string.IsNullOrEmpty(cls.HitPointsPerLevel)) head += ", hit points per level " + cls.HitPointsPerLevel;
            table.AddHeaderRow(new TextElement(head, "heading"), null);

            Group(table, prefix + "Statistics", levels);
            AddStatRows(table, cls, levels);

            var markers = cls.SpellbookProgressionVM?.MainChupaChupsList;
            if (markers != null && markers.Count > 0)
            {
                Group(table, prefix + "Spells", levels);
                AddSpellRow(table, markers, levels);
            }

            if (cls.ProgressionVms != null)
                foreach (var inner in cls.ProgressionVms)
                {
                    if (inner == null) continue;
                    if (!HasLines(inner.MainChupaChupsLines) && !HasDets(inner.AdditionalChupaChupsList)) continue;
                    var name = string.IsNullOrEmpty(inner.ProgressionName) ? "Features" : inner.ProgressionName;
                    Group(table, prefix + name, levels);
                    AddDeterminators(table, inner.AdditionalChupaChupsList); // level-1 foundational features first
                    AddFeatureRows(table, inner.MainChupaChupsLines, levels);
                }
        }

        // A block header row: the group name (leftmost) + a fresh level ruler. The ruler is restated for
        // every block (matching the game); fresh TextElements each time since each cell is a real child.
        private static void Group(Table table, string name, IList<LevelProgressionEntryVM> levels)
        {
            var cols = new List<UIElement>(levels.Count);
            foreach (var lv in levels) cols.Add(new TextElement("Level " + lv.Level));
            table.AddHeaderRow(new TextElement(name ?? "", "heading"), cols);
        }

        // BAB + the three saves, one dense row each (value at every level). Headers carry the glossary
        // tooltip so Space on the row header explains the stat. Archetype progressions override the
        // class's where present (matching the game's martial-stats logic).
        private static void AddStatRows(Table table, ClassProgressionVM cls, IList<LevelProgressionEntryVM> levels)
        {
            var bp = ClassField?.GetValue(cls) as BlueprintCharacterClass;
            if (bp == null) return;
            var arch = ArchetypeField?.GetValue(cls) as BlueprintArchetype;
            AddStatRow(table, "Base attack bonus", arch?.BaseAttackBonus ?? bp.BaseAttackBonus, levels, "BaseAttackBonus");
            AddStatRow(table, "Fortitude", arch?.FortitudeSave ?? bp.FortitudeSave, levels, "SaveFortitude");
            AddStatRow(table, "Reflex", arch?.ReflexSave ?? bp.ReflexSave, levels, "SaveReflex");
            AddStatRow(table, "Will", arch?.WillSave ?? bp.WillSave, levels, "SaveWill");
        }

        private static void AddStatRow(Table table, string name, BlueprintStatProgression prog,
            IList<LevelProgressionEntryVM> levels, string glossaryKey)
        {
            if (prog == null) return;
            var cells = new List<UIElement>(levels.Count);
            foreach (var lv in levels)
            {
                int b = prog.GetBonus(lv.Level);
                cells.Add(new TextElement(b >= 0 ? "+" + b : b.ToString()));
            }
            table.AddDataRow(new TextElement(name, null, new TooltipTemplateGlossary(glossaryKey)), cells);
        }

        // One "Spells" row mirroring the game: a cell at each caster level showing the highest spell
        // level reachable there (running max), with that level's per-day breakdown as a Space drill-in.
        // (The game only places a marker once you reach 1st-level spells, so cantrip-only levels are blank.)
        private static void AddSpellRow(Table table, IList<SpellbookProgressionChupaChupsVM> markers,
            IList<LevelProgressionEntryVM> levels)
        {
            var cellAt = new Dictionary<int, UIElement>();
            int runningMax = 0;
            foreach (var ch in markers) // ordered by character level
            {
                if (ch == null) continue;
                if (ch.SpellLevel.HasValue) runningMax = ch.SpellLevel.Value;
                cellAt[ch.Level] = new TextElement(Ordinal(runningMax) + "-level spells", null, ch.Tooltip);
            }

            var cells = new List<UIElement>(levels.Count);
            foreach (var lv in levels) cells.Add(cellAt.TryGetValue(lv.Level, out var c) ? c : null);
            table.AddDataRow(new TextElement("Spells"), cells);
        }

        private static string Ordinal(int n)
        {
            int mod100 = n % 100;
            string suffix = (mod100 >= 11 && mod100 <= 13) ? "th"
                : (n % 10 == 1) ? "st" : (n % 10 == 2) ? "nd" : (n % 10 == 3) ? "rd" : "th";
            return n + suffix;
        }

        private static bool HasLines(IEnumerable<List<FeatureProgressionChupaChupsVM>> lines)
        {
            if (lines == null) return false;
            foreach (var l in lines) if (l != null && l.Count > 0) return true;
            return false;
        }

        private static bool HasDets(IList<FeatureProgressionChupaChupsVM> dets)
        {
            if (dets == null) return false;
            foreach (var d in dets) if (d != null) return true;
            return false;
        }

        // Determinators (the game's AdditionalChupaChupsList): foundational level-1 features with no
        // per-level progression, so each is a single labeled cell (name + rank + drill-in), no level data.
        private static void AddDeterminators(Table table, IList<FeatureProgressionChupaChupsVM> dets)
        {
            if (dets == null) return;
            foreach (var ch in dets)
                if (ch != null) table.AddDataRow(Cell(ch), null);
        }

        // A labeled band (Feats / Shared) as its own group: a header row + determinators + feature rows,
        // if it has any content.
        private static void AddBand(Table table, string name,
            IEnumerable<List<FeatureProgressionChupaChupsVM>> lines,
            IList<FeatureProgressionChupaChupsVM> additional, IList<LevelProgressionEntryVM> levels)
        {
            if (!HasLines(lines) && !HasDets(additional)) return;
            Group(table, name, levels);
            AddDeterminators(table, additional);
            AddFeatureRows(table, lines, levels);
        }

        // One row per game line, mirroring the game's packing (≤1 feature per cell, packing invariant).
        // A rank-chain keeps its feature name; a packed row of unrelated features reads "Features".
        private static void AddFeatureRows(Table table,
            IEnumerable<List<FeatureProgressionChupaChupsVM>> lines, IList<LevelProgressionEntryVM> levels)
        {
            foreach (var line in lines)
            {
                if (line == null || line.Count == 0) continue;

                string header = null;
                bool mixed = false;
                var byLevel = new Dictionary<int, FeatureProgressionChupaChupsVM>();
                foreach (var ch in line)
                {
                    if (ch == null) continue;
                    byLevel[ch.Level] = ch;
                    if (string.IsNullOrEmpty(ch.Name)) continue;
                    if (header == null) header = ch.Name;
                    else if (header != ch.Name) mixed = true;
                }

                var cells = new List<UIElement>(levels.Count);
                foreach (var lv in levels)
                    cells.Add(byLevel.TryGetValue(lv.Level, out var ch) ? Cell(ch) : null);

                table.AddDataRow(new TextElement(mixed ? "Features" : (header ?? "Feature")), cells);
            }
        }

        private static UIElement Cell(FeatureProgressionChupaChupsVM ch)
        {
            var text = ch.Name ?? "";
            if (ch.HasRank && !string.IsNullOrEmpty(ch.Rank)) text += " " + ch.Rank;
            if (ch.DifType == ClassArchetypeDifType.Added) text += " (added)";
            else if (ch.DifType == ClassArchetypeDifType.Removed) text += " (removed)";
            return new TextElement(text, null, ch.Tooltip); // Space drills into the feature write-up
        }
    }
}
