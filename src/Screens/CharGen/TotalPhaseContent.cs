using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings; // UIStrings / UITextCharSheet (localized labels)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Total;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.BAB; // CharInfoBABVM (decompiler-skipped; members reconstructed from its view)
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The final chargen phase — "results" (<see cref="CharGenTotalPhaseVM"/>): the full character
    /// sheet. It's built from the game's reusable CharInfo* section VMs, so the stat readers (via
    /// <see cref="ICharSheetLayout"/> + <see cref="CharInfoStatRows"/>) are reused when we build the
    /// in-game character sheet. The Complete button isn't part of this content — it's the wizard-level
    /// Next/Complete (always enabled here), handled by <see cref="CharGenScreen"/>.
    ///
    /// Each section is one Tab-stop (Tab between, arrow within); Space on a stat/feature/class drills
    /// into its tooltip. We mirror what the detailed view actually binds — so e.g. Size, which the VM
    /// builds but the view doesn't show, is omitted. List sections that are empty for this character
    /// (no DR, no spellbook, …) are skipped rather than shown blank.
    /// </summary>
    public sealed class TotalPhaseContent : CharGenPhaseContent<CharGenTotalPhaseVM>
    {
        // The accessible presentation of the stat groups. Swap this (later, from a setting) to re-shape
        // the sheet — grid vs. flat, regrouped — without touching how the data below is assembled.
        private readonly ICharSheetLayout _layout = new GridCharSheetLayout();

        public TotalPhaseContent(CharGenTotalPhaseVM phase) : base(phase) { }

        private static UITextCharSheet S => UIStrings.Instance.CharacterSheet;

        public override void Build(Container content)
        {
            BuildSummary(content);

            // Ability Scores — Score + Modifier as a grid (no localized group title; the game shows an
            // unlabeled block).
            if (Phase.AbilityScores?.AbilityScores != null)
            {
                var g = new StatGroup("Ability Scores", "Score", "Modifier");
                foreach (var a in Phase.AbilityScores.AbilityScores) g.Row(CharInfoStatRows.Ability(a));
                content.Add(_layout.Build(g));
            }

            // Skills — Rank + Modifier as a grid.
            if (Phase.SkillBlock?.Skills != null)
            {
                var g = new StatGroup((string)S.Skills, "Rank", "Modifier");
                foreach (var sk in Phase.SkillBlock.Skills) g.Row(CharInfoStatRows.Skill(sk));
                content.Add(_layout.Build(g));
            }

            BuildDefense(content);
            BuildAttack(content);
            BuildCombat(content);
            BuildClasses(content);
            BuildFeatures(content);
            BuildSpells(content);
            BuildWeaponProficiency(content);
            BuildDamageReduction(content);
            BuildEnergyResistance(content);
        }

        // Race / gender / alignment and HP — free-form lines, each with the game's tooltip.
        private void BuildSummary(Container content)
        {
            var summary = new ListContainer((string)S.Summary);
            var rga = Phase.RaceGenderAlignment;
            if (rga != null)
            {
                summary.Add(new TextElement(() => rga.RaceValue, tooltip: () => rga.RaceTooltip));
                summary.Add(new TextElement(() => rga.GenderValue, tooltip: () => rga.GenderTooltip));
                summary.Add(new TextElement(() => rga.AlignmentDisplayValue, tooltip: () => rga.AlignmentTooltip));
            }
            if (Phase.HitPoints != null)
                summary.Add(new TextElement(() => Labeled((string)S.HP, Phase.HitPoints.HpText.Value),
                    tooltip: () => Phase.HitPoints.Tooltip.Value));
            content.Add(summary);
        }

        // AC / Touch / Flat-footed, the three saves, and Speed — single value each (a flat list).
        private void BuildDefense(Container content)
        {
            var g = new StatGroup((string)S.Defense);
            var ac = Phase.ArmorClass;
            if (ac != null)
            {
                g.Row(CharInfoStatRows.Value(ac.AC, signed: false));
                g.Row(CharInfoStatRows.Value(ac.Touch, signed: false));
                g.Row(CharInfoStatRows.Value(ac.FlatFooted, signed: false));
            }
            var st = Phase.SavingThrow;
            if (st != null)
            {
                g.Row(CharInfoStatRows.Value(st.Fortitude, signed: true));
                g.Row(CharInfoStatRows.Value(st.Reflex, signed: true));
                g.Row(CharInfoStatRows.Value(st.Will, signed: true));
            }
            if (Phase.Speed != null) g.Row(CharInfoStatRows.Value(Phase.Speed, signed: false));
            if (g.Rows.Count > 0) content.Add(_layout.Build(g));
        }

        // Base attack bonus — Main / Melee / Ranged, each an attack string like "+6/+1". CharInfoBABVM
        // was skipped by the decompiler; its public members (Type, BabValue, Tooltip) come from its view.
        private void BuildAttack(Container content)
        {
            var list = new ListContainer((string)S.Attack);
            AddBab(list, Phase.MainBAB, (string)S.BAB);
            AddBab(list, Phase.MeleeBAB, (string)S.BABMelee);
            AddBab(list, Phase.RangedBAB, (string)S.BABRanged);
            if (list.Children.Count > 0) content.Add(list);
        }

        private void AddBab(ListContainer list, CharInfoBABVM bab, string label)
        {
            if (bab == null) return;
            list.Add(new TextElement(() => label + ", " + BabString(bab), tooltip: () => bab.Tooltip));
        }

        // Mirrors CharInfoBABView.FillData: first attack always signed; later ones show "-" when <= 0.
        private static string BabString(CharInfoBABVM bab)
        {
            var vals = bab.BabValue;
            if (vals == null || vals.Count == 0) return "+0";
            var parts = new List<string>(vals.Count);
            for (int i = 0; i < vals.Count; i++)
                parts.Add((vals[i] <= 0 && i != 0) ? "-" : Signed(vals[i]));
            return string.Join("/", parts);
        }

        // Combat Maneuver (CMB / CMD), Initiative, Spell Resistance — single value each.
        private void BuildCombat(Container content)
        {
            var g = new StatGroup((string)S.MartialQualities);
            var cm = Phase.CombatManeuver;
            if (cm != null)
            {
                // The VM names CMB/CMD just "Bonus"/"Defense"; prefix so they're unambiguous out of context.
                g.Row(CharInfoStatRows.Value(cm.CMB, signed: true, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Bonus));
                g.Row(CharInfoStatRows.Value(cm.CMD, signed: false, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Defense));
            }
            if (Phase.Initiative != null) g.Row(CharInfoStatRows.Value(Phase.Initiative, signed: true, nameOverride: (string)S.Initiative));
            if (Phase.SpellResistance != null) g.Row(CharInfoStatRows.Value(Phase.SpellResistance, signed: false));
            if (g.Rows.Count > 0) content.Add(_layout.Build(g));
        }

        // Classes (and mythic) — "Name level", each drilling into the class tooltip.
        private void BuildClasses(Container content)
        {
            var vms = Phase.Classes?.ClassVMs;
            if (vms == null || vms.Count == 0) return;
            var list = new ListContainer((string)S.Class);
            foreach (var c in vms)
            {
                var entry = c;
                list.Add(new TextElement(() => entry.ClassName + " " + entry.Level, tooltip: () => entry.Tooltip));
            }
            content.Add(list);
        }

        // Features, feats, traits (and, in chargen, newly-known spells) — grouped under headings in one
        // list; each feature drills into its full description tooltip.
        private void BuildFeatures(Container content)
        {
            var groups = Phase.Abilities?.ShowGroupList;
            if (groups == null) return;
            var list = new ListContainer((string)S.FeaturesAndAbilitites);
            foreach (var group in groups)
            {
                if (group == null || group.IsEmpty) continue;
                if (!string.IsNullOrEmpty(group.Label)) list.Add(new TextElement(group.Label, "heading"));
                foreach (var f in group.FeatureList)
                {
                    var feat = f;
                    list.Add(new TextElement(() => FeatureName(feat), tooltip: () => feat.Tooltip));
                }
            }
            if (list.Children.Count > 0) content.Add(list);
        }

        // Spells per day, by spellbook and level (cantrips are at-will). The known/prepared spells
        // themselves show under Features (the chargen "new spells" group) and the Spells phase.
        private void BuildSpells(Container content)
        {
            var books = Phase.SpellTables?.SpellbookTables;
            if (books == null || books.Count == 0) return;
            var list = new ListContainer((string)S.Spells);
            foreach (var book in books)
            {
                var b = book;
                list.Add(new TextElement(() => SpellbookLine(b)));
            }
            content.Add(list);
        }

        private void BuildWeaponProficiency(Container content)
        {
            var data = Phase.WeaponProficiency?.Data;
            if (data == null || data.Count == 0) return;
            var list = new ListContainer((string)S.WeaponProficiency);
            foreach (var e in data)
            {
                var entry = e;
                list.Add(new TextElement(() => entry.DisplayName));
            }
            content.Add(list);
        }

        private void BuildDamageReduction(Container content)
        {
            var data = Phase.DamageReduction?.Data;
            if (data == null || data.Count == 0) return;
            var list = new ListContainer((string)S.DamageReduction);
            foreach (var e in data)
            {
                var entry = e;
                list.Add(new TextElement(() => entry.Value + "/" + string.Join(", ", entry.Exceptions.ToArray())));
            }
            content.Add(list);
        }

        private void BuildEnergyResistance(Container content)
        {
            var data = Phase.EnergyResistance?.Data;
            if (data == null || data.Count == 0) return;
            var list = new ListContainer((string)S.EnergyRsistance);
            foreach (var e in data)
            {
                var entry = e;
                list.Add(new TextElement(() => entry.Immunity ? entry.Type + ", immunity" : entry.Type + " " + entry.Value));
            }
            content.Add(list);
        }

        private static string FeatureName(Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Abilities.CharInfoFeatureVM f)
        {
            // Match the visible badge: rank shows only when stacked (> 1).
            if (f.Rank.HasValue && f.Rank.Value > 1) return f.DisplayName + " " + f.Rank.Value;
            return f.DisplayName;
        }

        private static string SpellbookLine(Kingmaker.UI.MVVM._VM.ServiceWindows.MythicInfo.CharInfoSpellTableVM book)
        {
            var t = book.SpellTable;
            if (t == null || t.Count == 0) return book.SpellbookName;
            var parts = new List<string> { "cantrips " + Cantrips(t[0]) };
            for (int i = 1; i < t.Count; i++) parts.Add("level " + i + " " + t[i]);
            return book.SpellbookName + ": " + string.Join(", ", parts.ToArray());
        }

        // SpellTable[0] is the cantrip cell: an infinity sprite (at-will) or "-" (none). The sprite tag
        // would strip to nothing for speech, so translate it; leave "-" as-is.
        private static string Cantrips(string cell)
        {
            if (!string.IsNullOrEmpty(cell) && (cell.Contains("sprite") || cell.Contains("Infinity"))) return "at will";
            return string.IsNullOrEmpty(cell) ? "-" : cell;
        }

        private static string Labeled(string label, string value)
            => string.IsNullOrEmpty(value) ? label : label + ", " + value;

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
    }
}
