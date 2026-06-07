using System.Collections.Generic;
using System.Text;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings / UITextCharSheet (localized labels)
using Kingmaker.UI.Common; // UIUtility.AddSign, UIUtilityItem.AttackData
using Kingmaker.UI.MVVM._PCView.ServiceWindows.CharacterInfo; // CharInfoComponentType, CharInfoPageType (enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Abilities;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Alignment;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.BuffsAndConditions;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Attack;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.BAB;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Defence;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.NameAndPortrait;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main; // UnitProgressionVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Skills;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Stories;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary (section-header glossary tooltips)
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game character sheet (CharacterInfo service window). Page-based: a tab list of the PC pages
    /// (Summary / Abilities / Martial / Progression / Biography) from <see cref="CharInfoMenuVM"/>, plus a
    /// content area built from the live component blocks the game populated for the current page
    /// (<c>CharacterInfoVM.ComponentVMs</c>). Stat rendering reuses our CharSheet sink (the chargen "Total"
    /// screen is the same sheet). Tabs are stable; only the content refills on a page switch, so tab focus
    /// survives. Blocks are rendered in the game's real per-page order/set via
    /// <see cref="CharInfoWindowUtility.GetComponentsList"/> (unit-type-aware); the two identical attack
    /// blocks the Martial page carries are de-duplicated. Escape closes the window.
    /// </summary>
    public sealed class CharacterInfoScreen : Screen
    {
        public override string Key => "service.Character";
        public override string ScreenName => "Character";
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.CharacterInfo;

        private Container _content;
        private bool _built;
        private string _sig;

        public override void OnPush() { _built = false; _sig = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell(vm);
            var sig = ComponentSig(vm); // changes when the page's block set changes
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static CharacterInfoVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.CharacterInfoVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        private static string ComponentSig(CharacterInfoVM vm)
        {
            // Keyed on the current page + which component VMs are live, so the content refills exactly when
            // the page (and thus its block set + order) changes, but not on every frame.
            var sb = new StringBuilder();
            sb.Append((int)(vm.CurrentPage?.Value ?? CharInfoPageType.None)).Append(':');
            foreach (var kv in vm.ComponentVMs)
                if (kv.Value?.Value != null) sb.Append((int)kv.Key).Append(',');
            return sb.ToString();
        }

        // The blocks shown on the current page, in the game's real order. CharInfoWindowUtility.PagesContent
        // is the source of truth (decoded from its static cctor): pages Summary/Abilities/Martial/Progression
        // all lead with the persistent summary panel [NameAndPortrait, LevelClassScores, AttackMain,
        // DefenceMain] then add page-specific blocks; the set is also unit-type-aware (Summary appends
        // AlignmentWheel for the PC, Stories for a companion, Martial for a pet). We call the game's own
        // GetComponentsList rather than a hand-built order so it stays faithful (e.g. Biography is
        // NameFullPortrait → AlignmentWheel → history, not the enum order).
        private static List<CharInfoComponentType> PageComponents(CharacterInfoVM vm)
        {
            var unit = vm.UnitDescriptor?.Value;
            if (unit == null) return new List<CharInfoComponentType>();
            var page = vm.CurrentPage?.Value ?? CharInfoPageType.None;
            return CharInfoWindowUtility.GetComponentsList(page, unit) ?? new List<CharInfoComponentType>();
        }

        private void BuildShell(CharacterInfoVM vm)
        {
            _built = true;
            Clear();

            // Page tabs — the game's page radio group; activating selects the page (the content then refills).
            var menu = vm.CharInfoMenuVM;
            var entities = menu?.SelectionGroup?.EntitiesCollection;
            if (entities != null)
            {
                var tabs = new ListContainer("Pages");
                foreach (var e in entities)
                {
                    var ent = e;
                    tabs.Add(new ProxySelectionItem(ent, () => PageName(ent.PageType), role: "tab"));
                }
                Add(tabs);
            }

            _content = new Panel();
            Add(_content);
            Navigation.Attach(this);
        }

        private void RefillContent(CharacterInfoVM vm)
        {
            if (_content == null) return;
            _content.Clear();
            var sink = new FlowSheetCharSheetSink();
            UIElement progression = null; // the Progression block is its own FlowSheet (own Tab-stop), not a sink section
            bool attacksShown = false;    // Martial has both AttackMain (summary) + AttackMartial (detail), same VM/data — show once
            // Render in the game's real per-page order (CharInfoWindowUtility.GetComponentsList).
            foreach (var type in PageComponents(vm))
            {
                if (!vm.ComponentVMs.TryGetValue(type, out var rp) || rp?.Value == null) continue;
                var block = rp.Value;
                if (block is CharInfoAttacksBlockVM)
                {
                    if (attacksShown) continue;
                    attacksShown = true;
                }
                if (block is UnitProgressionVM prog)
                    progression = ProgressionGrid.Build(prog, null, new ProgressionGrid.Options { AllClassBands = true });
                else
                    RenderBlock(type, block, sink);
            }
            // Only add the sheet if a section was actually rendered (the Progression page has no sink blocks).
            if (sink.Build() is FlowSheet sheet && sheet.RowCount > 0) _content.Add(sheet);
            if (progression != null) _content.Add(progression);
        }

        private static void RenderBlock(CharInfoComponentType type, CharInfoComponentVM block, ICharSheetSink sink)
        {
            switch (block)
            {
                case CharInfoNameAndPortraitVM np: RenderNamePortrait(np, sink); break;
                case CharInfoLevelClassScoresVM lcs: RenderLevelClassScores(lcs, sink); break;
                case CharInfoAttacksBlockVM atk: RenderAttacks(atk, sink); break;
                case CharInfoDefenceBlockVM def: RenderDefence(def, sink); break;
                case CharInfoSkillsBlockVM sk: RenderSkills(sk, sink); break;
                case CharInfoAbilitiesVM ab: RenderFeatureGroups(ab.ShowGroupList, "Abilities", sink); break;
                case CharInfoBuffsAndConditionsVM bc: RenderFeatureGroups(bc.ShowGroupList, "Buffs and conditions", sink); break;
                case CharInfoMartialVM mt: RenderMartial(mt, sink); break;
                case CharInfoAlignmentVM al: RenderAlignment(type, al, sink); break;
                case CharInfoStoriesVM st: RenderStories(st, sink); break;
                default: sink.ListSection(type.ToString(), new[] { new TextElement("Not shown yet.") }); break;
            }
        }

        // CharInfoAlignmentVM backs three blocks: the wheel (current alignment + mythic level) and the
        // history list (AlignmentHistory + the Biography page's copy). We mirror each view's bound text —
        // the wheel shows GetAlignmentName/AlignmentUndetectable + MythicLevel (Deity/BirthDay are computed
        // but read by no view, so omitted); the history shows "Alignment shifted <direction>: <description>".
        private static void RenderAlignment(CharInfoComponentType type, CharInfoAlignmentVM al, ICharSheetSink sink)
        {
            if (type == CharInfoComponentType.AlignmentWheel)
            {
                var items = new List<UIElement>();
                items.Add(new TextElement(() => "Alignment: " + AlignmentText(al)));
                if (!string.IsNullOrEmpty(al.MythicLevel)) items.Add(new TextElement(() => "Mythic: " + al.MythicLevel));
                sink.ListSection("Alignment", items);
                return;
            }
            // History (AlignmentHistory / BiographyAlignmentHistory).
            var hist = al.AlignmentHistory;
            if (hist == null || hist.Count == 0) return;
            var lines = new List<UIElement>();
            string shifted = (string)S.AlignmentShifted;
            foreach (var rec in hist)
            {
                var r = rec;
                lines.Add(new TextElement(() => shifted + " " + (string)UIUtility.GetAlignmentShiftDirectionText(r.Direction)
                    + (string.IsNullOrEmpty(r.Description) ? "" : ": " + r.Description)));
            }
            sink.ListSection((string)S.History, lines);
        }

        private static string AlignmentText(CharInfoAlignmentVM al)
            => al.IsUndetectable ? (string)S.AlignmentUndetectable : (string)UIUtility.GetAlignmentName(al.CurrentAlignment);

        // Companion stories — each a title (heading) + its text. Matches CharInfoStoriesVM.Stories.
        private static void RenderStories(CharInfoStoriesVM st, ICharSheetSink sink)
        {
            if (st.Stories == null || st.Stories.Count == 0) return;
            var items = new List<UIElement>();
            foreach (var story in st.Stories)
            {
                var s = story;
                if (!string.IsNullOrEmpty(s.Title)) items.Add(new TextElement(s.Title, "heading"));
                if (!string.IsNullOrEmpty(s.StoryText)) items.Add(new TextElement(() => s.StoryText));
            }
            if (items.Count > 0) sink.ListSection("Stories", items);
        }

        private static void RenderNamePortrait(CharInfoNameAndPortraitVM np, ICharSheetSink sink)
        {
            var items = new List<UIElement> { new TextElement(() => "Name: " + np.UnitName) };
            var mythic = np.MythicName?.Value;
            if (!string.IsNullOrEmpty(mythic)) items.Add(new TextElement(() => "Mythic: " + np.MythicName.Value));
            if (np.HitPoints != null)
                items.Add(new TextElement(() => "Hit points: " + np.HitPoints.HpText.Value,
                    tooltip: () => np.HitPoints.Tooltip.Value));
            sink.ListSection("Character", items);
        }

        private static void RenderLevelClassScores(CharInfoLevelClassScoresVM lcs, ICharSheetSink sink)
        {
            // Visual order: header info (level, race/gender/alignment) reads first, then the block's
            // ability scores, then the class list (prefab puts AbilityScores above ClassesList).
            var xp = lcs.Experience;
            if (xp != null)
            {
                var items = new List<UIElement> { new TextElement(() => "Level: " + xp.Level) };
                items.Add(new TextElement(() => "Experience: " + xp.CurrentExp + " / " + xp.NextLevelExp));
                if (xp.NegativeLevels > 0) items.Add(new TextElement(() => "Negative levels: " + xp.NegativeLevels));
                sink.ListSection("Level", items);
            }

            var rga = lcs.RaceGenderAlignment;
            if (rga != null)
                sink.ListSection("Race", new List<UIElement>
                {
                    new TextElement(() => "Race: " + rga.RaceValue, tooltip: () => rga.RaceTooltip),
                    new TextElement(() => "Gender: " + rga.GenderValue),
                    new TextElement(() => "Alignment: " + rga.AlignmentDisplayValue, tooltip: () => rga.AlignmentTooltip),
                });

            if (lcs.AbilityScores?.AbilityScores != null)
            {
                var g = new StatGroup("Ability Scores", "Score", "Modifier");
                foreach (var a in lcs.AbilityScores.AbilityScores) g.Row(CharInfoStatRows.Ability(a)); // carries the stat tooltip
                sink.StatGroup(g);
            }

            var classes = lcs.Classes?.ClassVMs;
            if (classes != null && classes.Count > 0)
            {
                var items = new List<UIElement>();
                foreach (var c in classes) { var cc = c; items.Add(new TextElement(() => cc.ClassName + " " + cc.Level, tooltip: () => cc.Tooltip)); }
                sink.ListSection("Classes", items);
            }
        }

        private static void RenderAttacks(CharInfoAttacksBlockVM atk, ICharSheetSink sink)
        {
            var g = new StatGroup("Attacks", "Attack", "Damage", "Crit"); // prefab columns: weapon, attack, damage, crit
            AddAttackRow(g, atk.MainHandAttack);
            AddAttackRow(g, atk.OffHandAttack);
            if (atk.AdditionalAttackEntities != null)
                foreach (var a in atk.AdditionalAttackEntities) AddAttackRow(g, a);
            if (g.Rows.Count > 0) sink.StatGroup(g);
            else sink.ListSection("Attacks", new[] { new TextElement("No attacks.") });
        }

        private static void AddAttackRow(StatGroup g, CharInfoAttackEntityVM a)
        {
            if (a == null || string.IsNullOrEmpty(a.AttackName)) return;
            // The *Label properties are the column labels; the actual values live in AttackData (the view
            // does SetData(AttackData) for the value + SetLabel(*Label) for the heading).
            g.Row(new StatRow(() => a.AttackName,
                new System.Func<string>[] { () => Attacks(a.AttackData), () => a.AttackData?.Damage ?? "", () => Crit(a.AttackData) },
                () => a.AttackTooltip));
        }

        private static string Attacks(UIUtilityItem.AttackData d) // e.g. "+6/+1"
            => d?.Attacks == null ? "" : string.Join("/", d.Attacks.Select(n => UIUtility.AddSign(n)));

        private static string Crit(UIUtilityItem.AttackData d) // threat range + multiplier, e.g. "19-20 x2"
        {
            if (d == null) return "";
            var s = d.CritChance ?? "";
            if (!string.IsNullOrEmpty(d.CritDamage)) s = (s.Length > 0 ? s + " " : "") + d.CritDamage;
            return s;
        }

        private static void RenderDefence(CharInfoDefenceBlockVM def, ICharSheetSink sink)
        {
            var g = new StatGroup("Defense");
            var ac = def.ArmorClass?.Value;
            if (ac != null)
            {
                // Prefab order: AC, Flat-footed, Touch (verified via the layout dump).
                g.Row(CharInfoStatRows.Value(ac.AC, signed: false));
                g.Row(CharInfoStatRows.Value(ac.FlatFooted, signed: false));
                g.Row(CharInfoStatRows.Value(ac.Touch, signed: false));
            }
            var st = def.SavingThrow?.Value;
            if (st != null)
            {
                g.Row(CharInfoStatRows.Value(st.Fortitude, signed: true));
                g.Row(CharInfoStatRows.Value(st.Reflex, signed: true));
                g.Row(CharInfoStatRows.Value(st.Will, signed: true));
            }
            g.Row(CharInfoStatRows.Value(def.Initiative?.Value, signed: true));
            g.Row(CharInfoStatRows.Value(def.Speed?.Value, signed: false));
            g.Row(CharInfoStatRows.Value(def.Size?.Value, signed: false));
            sink.StatGroup(g);
        }

        private static void RenderSkills(CharInfoSkillsBlockVM sk, ICharSheetSink sink)
        {
            if (sk.Skills == null) return;
            var g = new StatGroup("Skills", "Rank", "Modifier");
            foreach (var s in sk.Skills) g.Row(CharInfoStatRows.Skill(s));
            sink.StatGroup(g);
        }

        private static UITextCharSheet S => UIStrings.Instance.CharacterSheet;

        // The Martial page's composite block. We mirror CharInfoMartialPCView.RefreshView's bind order
        // exactly (it does NOT bind the VM's DefenceBlock — defence shows as its own DefenceMain block):
        // BAB (main/melee/ranged), Initiative, Spell Resistance, Combat Maneuver, Weapon Proficiency,
        // Damage Reduction, Energy Resistance. BAB/proficiency/DR/resistance reuse the chargen patterns.
        private static void RenderMartial(CharInfoMartialVM m, ICharSheetSink sink)
        {
            var bab = new List<UIElement>();
            AddBab(bab, m.MainBab, (string)S.BAB);
            AddBab(bab, m.MeleeBab, (string)S.BABMelee);
            AddBab(bab, m.RangedBab, (string)S.BABRanged);
            if (bab.Count > 0) sink.ListSection((string)S.Attack, bab);

            var g = new StatGroup((string)S.MartialQualities);
            g.Row(CharInfoStatRows.Value(m.Initiative, signed: true));
            g.Row(CharInfoStatRows.Value(m.SpellResistance, signed: false));
            var cm = m.CombatManeuver;
            if (cm != null)
            {
                // The VM names CMB/CMD just "Bonus"/"Defense"; prefix so they're unambiguous out of context.
                g.Row(CharInfoStatRows.Value(cm.CMB, signed: true, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Bonus));
                g.Row(CharInfoStatRows.Value(cm.CMD, signed: false, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Defense));
            }
            sink.StatGroup(g);

            // Each of these sections' only tooltip in-game is a glossary on its header label
            // (m_Label.SetGlossaryTooltip — "WeaponProficiency"/"DR"/"ER"); the entries themselves carry no
            // per-row tooltip and their per-feature description is deliberately not rendered (m_Description
            // unwired). The FlowSheet has no focusable header, so we surface the category glossary as each
            // entry's drill-in (the real glossary key, not invented text).
            var wp = m.WeaponProficiency?.Data;
            if (wp != null && wp.Count > 0)
            {
                var items = new List<UIElement>();
                foreach (var e in wp) { var entry = e; items.Add(new TextElement(() => entry.DisplayName, tooltip: () => new TooltipTemplateGlossary("WeaponProficiency"))); }
                sink.ListSection((string)S.WeaponProficiency, items);
            }

            var dr = m.DamageReduction?.Data;
            if (dr != null && dr.Count > 0)
            {
                var items = new List<UIElement>();
                foreach (var e in dr) { var entry = e; items.Add(new TextElement(() => entry.Value + "/" + string.Join(", ", entry.Exceptions.ToArray()), tooltip: () => new TooltipTemplateGlossary("DR"))); }
                sink.ListSection((string)S.DamageReduction, items);
            }

            var er = m.EnergyResistance?.Data;
            if (er != null && er.Count > 0)
            {
                var items = new List<UIElement>();
                foreach (var e in er) { var entry = e; items.Add(new TextElement(() => entry.Immunity ? entry.Type + ", immunity" : entry.Type + " " + entry.Value, tooltip: () => new TooltipTemplateGlossary("ER"))); }
                sink.ListSection((string)S.EnergyRsistance, items);
            }
        }

        private static void AddBab(List<UIElement> items, CharInfoBABVM bab, string label)
        {
            if (bab == null) return;
            items.Add(new TextElement(() => label + ", " + BabString(bab), tooltip: () => bab.Tooltip));
        }

        // Mirrors CharInfoBABView.FillData: first attack always signed; later ones show "-" when <= 0.
        private static string BabString(CharInfoBABVM bab)
        {
            var vals = bab.BabValue;
            if (vals == null || vals.Count == 0) return "+0";
            var parts = new List<string>(vals.Count);
            for (int i = 0; i < vals.Count; i++)
                parts.Add((vals[i] <= 0 && i != 0) ? "-" : (vals[i] >= 0 ? "+" + vals[i] : vals[i].ToString()));
            return string.Join("/", parts);
        }

        // Features / feats / special abilities / buffs — grouped under headings; each drills into its tooltip.
        // Same shape as chargen's BuildFeatures (CharInfoFeatureGroupVM list). Abilities and Buffs &
        // Conditions both render this way (both expose ShowGroupList of CharInfoFeatureGroupVM).
        private static void RenderFeatureGroups(List<CharInfoFeatureGroupVM> groups, string label, ICharSheetSink sink)
        {
            if (groups == null) return;
            var items = new List<UIElement>();
            foreach (var group in groups)
            {
                if (group == null || group.IsEmpty) continue;
                if (!string.IsNullOrEmpty(group.Label)) items.Add(new TextElement(group.Label, "heading"));
                foreach (var f in group.FeatureList)
                {
                    var feat = f;
                    items.Add(new TextElement(() => FeatureName(feat), tooltip: () => feat.Tooltip));
                }
            }
            if (items.Count > 0) sink.ListSection(label, items);
        }

        private static string FeatureName(CharInfoFeatureVM f) // rank shows only when stacked (>1), like the badge
            => f.Rank.HasValue && f.Rank.Value > 1 ? f.DisplayName + " " + f.Rank.Value : f.DisplayName;

        private static string PageName(CharInfoPageType p)
        {
            switch (p)
            {
                case CharInfoPageType.SummaryPC: return "Summary";
                case CharInfoPageType.AbilitiesPC: return "Abilities";
                case CharInfoPageType.MartialPC: return "Martial";
                case CharInfoPageType.ProgressionPC: return "Progression";
                case CharInfoPageType.BiographyPC: return "Biography";
                default: return p.ToString();
            }
        }
    }
}
