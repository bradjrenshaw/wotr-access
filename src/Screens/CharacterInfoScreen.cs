using System.Collections.Generic;
using System.Text;
using System.Linq;
using Kingmaker;
using Kingmaker.UI.Common; // UIUtility.AddSign, UIUtilityItem.AttackData
using Kingmaker.UI.MVVM._PCView.ServiceWindows.CharacterInfo; // CharInfoComponentType, CharInfoPageType (enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Abilities;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Attack;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Defence;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.NameAndPortrait;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Skills;
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
    /// survives. Summary blocks + Skills are rendered; the remaining blocks are placeholders pending per-block
    /// work. Escape closes the window.
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

        // Visual block order from the prefab (CharacterScreen sibling order; Attack+Defence sit in the
        // AttackDefenceMain sub-container). tools/charinfo_layout.py dumps this.
        private static readonly CharInfoComponentType[] BlockOrder =
        {
            CharInfoComponentType.NameAndPortrait, CharInfoComponentType.LevelClassScores,
            CharInfoComponentType.AttackMain, CharInfoComponentType.DefenceMain, CharInfoComponentType.Skills,
            CharInfoComponentType.BuffsAndConditions, CharInfoComponentType.Abilities, CharInfoComponentType.Martial,
            CharInfoComponentType.AttackMartial, CharInfoComponentType.AlignmentWheel,
            CharInfoComponentType.AlignmentHistory, CharInfoComponentType.Stories,
            CharInfoComponentType.NameFullPortrait, CharInfoComponentType.BiographyAlignmentHistory,
            CharInfoComponentType.Progression, CharInfoComponentType.BiographyStories,
        };

        private static CharacterInfoVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.CharacterInfoVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        private static string ComponentSig(CharacterInfoVM vm)
        {
            var sb = new StringBuilder();
            foreach (var kv in vm.ComponentVMs)
                if (kv.Value?.Value != null) sb.Append((int)kv.Key).Append(',');
            return sb.ToString();
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
            // Render in the prefab's visual top-to-bottom order (verified via tools/charinfo_layout.py),
            // not the dictionary's iteration order.
            foreach (var type in BlockOrder)
                if (vm.ComponentVMs.TryGetValue(type, out var rp) && rp?.Value != null)
                    RenderBlock(type, rp.Value, sink);
            _content.Add(sink.Build());
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
                case CharInfoAbilitiesVM ab: RenderAbilities(ab, sink); break;
                default: sink.ListSection(type.ToString(), new[] { new TextElement("Not shown yet.") }); break;
            }
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

        // Features / feats / special abilities — grouped under headings; each drills into its tooltip.
        // Same shape as chargen's BuildFeatures (CharInfoFeatureGroupVM list).
        private static void RenderAbilities(CharInfoAbilitiesVM ab, ICharSheetSink sink)
        {
            var groups = ab.ShowGroupList;
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
            if (items.Count > 0) sink.ListSection("Abilities", items);
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
