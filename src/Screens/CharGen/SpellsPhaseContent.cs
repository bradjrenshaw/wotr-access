using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (KnownSpell)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Spells;
using Kingmaker.UI.MVVM._VM.Other; // RecommendationType
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Switchers; // SpellbookLevelSwitcherEntityVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Spells phase: two parts. First, the picker — a multi-select check group with a slot budget: a
    /// live "spells to select" count, then a table of choosable spells (Level / School / Recommendation;
    /// the spell name is a toggle: Enter flips it, Space opens the tooltip), in the game's own order.
    /// Second, a "Known spells" reference mirroring the game's spellbook panel: radio buttons pick a
    /// spell level (only levels that have spells), and a list shows that level's known spells (keyed
    /// per level — the game's per-level AbilityDataVMs refresh as the level changes). Per-level is
    /// deliberate — a character can end up knowing hundreds of spells, so we never flatten them into
    /// one list. Everything is created lazily by the game; immediate mode renders it as it appears.
    /// </summary>
    public sealed class SpellsPhaseContent : CharGenPhaseContent<CharGenSpellsPhaseVM>
    {
        // Mirrors CharGenSpellsSelectorCheckPCView.EntityComparer so rows read in on-screen order.
        private static readonly IComparer<CharGenSpellSelectorItemVM> Order =
            Comparer<CharGenSpellSelectorItemVM>.Create((a, b) =>
            {
                int c = a.HasInSpellbook.CompareTo(b.HasInSpellbook); if (c != 0) return c; // known last
                c = b.Level.CompareTo(a.Level); if (c != 0) return c;                        // level descending
                c = b.Recommendation.Recommendation.Value.CompareTo(a.Recommendation.Recommendation.Value);
                if (c != 0) return c;                                                        // recommended first
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

        // Spell levels as words (matching how we label spell levels elsewhere), not the raw numbers the
        // game's tiny switcher shows; level 11 is the game's "Favorites" view.
        private static readonly string[] Words =
            { "Cantrips", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth" };

        public SpellsPhaseContent(CharGenSpellsPhaseVM phase) : base(phase) { }

        private bool NoSelections => Phase.SelectorMode == CharGenSpellsPhaseVM.SpellSelectorMode.NoSelections;

        public override void Build(GraphBuilder b, string k)
        {
            if (NoSelections)
            {
                b.AddItem(ControlId.Structural(k + "auto"), GraphNodes.Text(() => Loc.T("chargen.spells_auto")));
                BuildKnown(b, k);
                return;
            }

            // The sighted counter is a bare number under the game's "Choose spells" heading (slots
            // still open); we add the picked-of-total the roadmap widget shows as filled/empty slots.
            b.AddItem(ControlId.Structural(k + "count"), GraphNodes.Text(() =>
            {
                int total = Phase.MechanicSelectedSpells?.Length ?? 0;
                int chosen = Phase.SelectedSpellVMs?.Count ?? 0;
                var args = new { label = UIStrings.Instance.CharGen.ChooseSpells.ToString(),
                    available = Phase.AvailableSpellCount.Value, chosen, total };
                return Phase.AvailableSpellCount.Value <= 0
                    ? Loc.T("chargen.spells_all_chosen", args)
                    : Loc.T("chargen.spells_count", args);
            }));

            BuildSelector(b, k);
            BuildKnown(b, k);
        }

        // ----- the picker (choosable spells) -----

        private void BuildSelector(GraphBuilder b, string k)
        {
            var selector = Phase.SpellsSelector.Value;
            if (selector == null) return; // not in detailed view yet — renders once it materializes

            var items = selector.EntitiesCollection.OrderBy(v => v, Order).ToList();
            if (items.Count == 0) return;

            // A sheet table: column 0 is each spell's toggle; Level/School/Recommendation are parts on
            // the toggle (the row reads whole) and header-labelled cells for Left/Right. Rows key by
            // the item VM (picking doesn't reorder — HasInSpellbook only flips on commit).
            b.BeginStop("picker");
            var sheet = new GraphSheet(b, k + "pick:");
            sheet.Region(Loc.T("chargen.choose_spells"),
                new[] { Loc.T("col.level"), Loc.T("col.school"), Loc.T("col.recommendation") });
            foreach (var it in items)
            {
                var item = it; // capture for the live closures
                Func<string> level = () => item.Level.ToString();
                Func<string> school = () => School(item);
                Func<string> rec = () => RecLabel(item);
                var vt = new NodeVtable
                {
                    ControlType = ControlTypes.Toggle,
                    Announcements = new List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(() => item.DisplayName),
                        new NodeAnnouncement(() => Loc.T(item.IsSelected.Value ? "value.on" : "value.off"),
                            live: true, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(level),
                        new NodeAnnouncement(school),
                        new NodeAnnouncement(rec),
                        // Greyed for the sighted player once every slot is filled (or the spell is
                        // already known): IsAvailable false — the row can't be picked.
                        GraphNodes.DisabledPart(() => item.IsAvailable.Value || item.IsSelected.Value),
                    },
                    SearchText = () => item.DisplayName,
                    StateText = () => Loc.T(item.IsSelected.Value ? "value.on" : "value.off"),
                    // Tab into the picker lands on the first spell already toggled ON (a multi-select
                    // gets no "selected radio" landing; a filled slot is the natural place to start).
                    LandHere = () => item.IsSelected.Value,
                    // A toggle in a multi-select group (slot budget) — the same OnClick guard the view
                    // uses (CharGenSpellSelectorItemPCView.OnClick): nothing happens on an UNAVAILABLE
                    // row (all slots filled / already known — the game greys it), and a pick goes
                    // through the "level-up plans will drop" confirmation the game asks first.
                    OnActivate = () =>
                    {
                        if (!item.IsAvailable.Value || (item.IsSelected.Value && !item.AllowSwitchOff))
                        {
                            Tts.Speak(Loc.T("chargen.spell_unavailable"), interrupt: true);
                            return;
                        }
                        UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                        item.WarnLevelupPlansWillDropBeforeAction(() => item.SetSelectedFromView(!item.IsSelected.Value));
                    },
                    OnTooltip = () =>
                    {
                        var tpl = item.TooltipTemplate();
                        if (tpl != null) TooltipScreen.Open(tpl);
                    },
                };
                sheet.Row(vt, item, level, school, rec);
            }
            sheet.Finish();
        }

        // ----- the known-spells reference (mirrors the game's spellbook panel) -----

        private void BuildKnown(GraphBuilder b, string k)
        {
            var sb = Phase.SpellbookVM;
            if (sb == null || sb.CurrentSpellbook?.Value == null) return; // spellbook not built yet

            b.BeginStop("knownlevels").PushContext(Loc.T("chargen.known_spells"), role: null, positions: false);
            var levels = AvailableLevels().ToList();
            if (levels.Count == 0)
            {
                b.AddItem(ControlId.Structural(k + "nospells"), GraphNodes.Text(() => Loc.T("chargen.no_spells_yet")));
                b.PopContext();
                return;
            }

            b.PushContext(Loc.T("metamagic.spell_level"), "list");
            int li = 0;
            foreach (var e in levels)
            {
                var ent = e; // capture
                b.AddItem(ControlId.Referenced(ent, k + "klvl:" + li),
                    GraphNodes.SelectionItem(ent, () => LevelName(ent.SpellbookLevel.Level)));
                li++;
            }
            b.PopContext();
            b.PopContext();

            // The selected level's spells, keyed per level (the VM list refreshes a frame after a level
            // pick — focus is on the radio, and this just re-renders).
            string lk = k + "known:" + (sb.CurrentSpellbookLevel?.Value?.Level ?? -1) + ":";
            b.BeginStop("knownlist");
            var known = sb.SpellbookKnownSpellsVM?.KnownSpells;
            if (known == null || known.Count == 0)
            {
                b.AddItem(ControlId.Structural(lk + "none"),
                    GraphNodes.Text(() => Loc.T("chargen.no_spells_at_level")));
                return;
            }
            int i = 0;
            foreach (var vm in known)
            {
                if (vm == null) { i++; continue; }
                var v = vm; // capture
                b.AddItem(ControlId.Referenced(v, lk + i),
                    GraphNodes.Text(() => v.DisplayName, () => v.Tooltip));
                i++;
            }
        }

        private IEnumerable<SpellbookLevelSwitcherEntityVM> AvailableLevels()
        {
            var sw = Phase.SpellbookVM?.SpellbookLevelSwitcherVM;
            var entities = sw?.SelectionGroup?.EntitiesCollection;
            if (entities == null) return Enumerable.Empty<SpellbookLevelSwitcherEntityVM>();
            return entities.Where(e => e != null && e.IsAvailable.Value); // only levels with spells
        }

        // ----- shared cell text -----

        private static string LevelName(int level)
            => level == 0 ? "Cantrips"
             : level == 11 ? "Favorites"
             : level < Words.Length ? Words[level] + " level"
             : level + " level";

        private static string School(CharGenSpellSelectorItemVM v)
            => v.HasInSpellbook ? v.SchoolName + " / " + UIStrings.Instance.Tooltips.KnownSpell : v.SchoolName;

        // The game's marker words from its glossary (RecommendationText); Neutral shows no marker.
        private static string RecLabel(CharGenSpellSelectorItemVM v)
            => RecommendationText.Label(v.Recommendation.Recommendation.Value) ?? "";
    }
}
