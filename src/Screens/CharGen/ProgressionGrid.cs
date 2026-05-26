using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.ChupaChupses;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Level;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Maps the game's progression grid (<see cref="UnitProgressionVM"/>) onto a navigable
    /// <see cref="Table"/>: columns are levels 1..N; rows are grouped into bands — one per class (its
    /// per-level BAB/saves, then its feature lines), then Feats and any Shared tracks. A feature cell
    /// is the feature at that (line, level), with Added/Removed marker and a Space drill-in; a stat
    /// cell is the value at that level. The game packs rank-less features onto shared rows ("Features").
    /// </summary>
    internal static class ProgressionGrid
    {
        // Each ClassProgressionVM keeps its class/archetype private; we reflect them to compute the
        // per-level BAB/saves (the grid's stat chupachups only flag increases, not the numbers).
        private static readonly System.Reflection.FieldInfo ClassField =
            AccessTools.Field(typeof(ClassProgressionVM), "m_UnitClass");
        private static readonly System.Reflection.FieldInfo ArchetypeField =
            AccessTools.Field(typeof(ClassProgressionVM), "m_UnitArchetype");

        public static Table Build(UnitProgressionVM prog)
        {
            var levels = prog?.LevelProgressionVM?.EntryVms;
            if (levels == null || levels.Count == 0) return null;

            var table = new Table("Progression");
            var cols = new List<UIElement>(levels.Count);
            foreach (var lv in levels) cols.Add(new TextElement("Level " + lv.Level));
            table.SetColumnHeaders(cols);

            if (prog.ClassProgressionVms != null)
                foreach (var cls in prog.ClassProgressionVms)
                {
                    if (cls == null) continue;
                    table.AddRow(new TextElement(cls.Name ?? "Class", "heading"), null); // class section
                    AddStatRows(table, cls, levels);
                    AddFeatureRows(table, ClassLines(cls), levels);
                }

            if (prog.FeatProgressionVM != null)
                AddBand(table, "Feats", prog.FeatProgressionVM.MainChupaChupsLines, levels);

            if (prog.SharedProgressionVms != null)
                foreach (var sh in prog.SharedProgressionVms)
                    if (sh != null) AddBand(table, sh.ProgressionName ?? "Shared", sh.MainChupaChupsLines, levels);

            return table.RowCount > 0 ? table : null;
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
            table.AddRow(new TextElement(name, null, new TooltipTemplateGlossary(glossaryKey)), cells);
        }

        private static IEnumerable<List<FeatureProgressionChupaChupsVM>> ClassLines(ClassProgressionVM cls)
        {
            if (cls.ProgressionVms == null) yield break;
            foreach (var inner in cls.ProgressionVms)
                if (inner != null)
                    foreach (var line in inner.MainChupaChupsLines)
                        yield return line;
        }

        // A labeled band (Feats / Shared): a section row (added only if there's content) + feature rows.
        private static void AddBand(Table table, string name,
            IEnumerable<List<FeatureProgressionChupaChupsVM>> lines, IList<LevelProgressionEntryVM> levels)
        {
            if (lines == null) return;
            bool any = false;
            foreach (var l in lines) if (l != null && l.Count > 0) { any = true; break; }
            if (!any) return;
            table.AddRow(new TextElement(name ?? "", "heading"), null);
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

                table.AddRow(new TextElement(mixed ? "Features" : (header ?? "Feature")), cells);
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
