using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (KnownSpell)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Spells;
using Kingmaker.UI.MVVM._VM.Other; // RecommendationType
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Switchers; // SpellbookLevelSwitcherEntityVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.KnownSpells; // AbilityDataVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Spells phase: two parts. First, the picker — a multi-select check group with a slot budget: a
    /// live "spells to select" count, then a table of choosable spells (Level / School / Recommendation,
    /// spell name = checkbox; Enter toggles, Space opens the tooltip), in the game's own order. Second,
    /// a "Known spells" reference mirroring the game's spellbook panel: radio buttons pick a spell level
    /// (only levels that have spells), and a list shows that level's known spells (the game's own
    /// per-level AbilityDataVMs, which refresh as the level changes). Per-level is deliberate — a
    /// character can end up knowing hundreds of spells, so we never flatten them into one list.
    /// Everything is created lazily on entering detailed view; per-item state is read live.
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

        private Panel _selectorPanel;
        private bool _selBuilt;

        private Panel _knownSection; // "Known spells" — holds the level radio + the per-level list
        private Panel _knownList;    // the selected level's spells (rebuilt on change)
        private string _levelsSig;   // which levels have spells (rebuild the radio when this changes)
        private string _spellsSig;   // current level + its spells (rebuild the list when this changes)

        public SpellsPhaseContent(CharGenSpellsPhaseVM phase) : base(phase) { }

        private bool NoSelections => Phase.SelectorMode == CharGenSpellsPhaseVM.SpellSelectorMode.NoSelections;

        public override void Build(Container content)
        {
            if (!NoSelections)
                content.Add(new TextElement(() =>
                    Phase.AvailableSpellCount.Value <= 0
                        ? "All spells selected"
                        : "Spells to select: " + Phase.AvailableSpellCount.Value));

            _selectorPanel = new Panel();
            content.Add(_selectorPanel);
            FillSelector();

            _knownSection = new Panel("Known spells");
            content.Add(_knownSection);
        }

        public override void Tick()
        {
            if (!_selBuilt && (NoSelections || Phase.SpellsSelector.Value != null)) FillSelector();
            TickKnown();
        }

        // ----- the picker (choosable spells) -----

        private void FillSelector()
        {
            if (_selectorPanel == null) return;
            _selectorPanel.Clear();

            if (NoSelections)
            {
                _selBuilt = true;
                _selectorPanel.Add(new TextElement("This class knows all its spells automatically."));
                return;
            }

            var selector = Phase.SpellsSelector.Value;
            if (selector == null) return; // not in detailed view yet — Tick will retry
            _selBuilt = true;

            var items = selector.EntitiesCollection.OrderBy(v => v, Order).ToList();
            if (items.Count == 0) return;

            var table = new Table("Choose spells");
            table.AddHeaderRow(new TextElement("Spell", "heading"), new UIElement[]
            {
                new TextElement("Level"), new TextElement("School"), new TextElement("Recommendation"),
            });
            foreach (var it in items)
            {
                var item = it; // capture for the live closures
                // A toggle in a multi-select group (slot budget): toggle via the same OnClick the view
                // uses — SetSelectedFromView(!IsSelected), gated on IsAvailable (AllowSwitchOff is true).
                table.AddDataRow(
                    new ProxySelectionItem(item, () => item.DisplayName, role: "toggle",
                        onActivate: () => item.SetSelectedFromView(!item.IsSelected.Value)),
                    new UIElement[]
                {
                    new TextElement(() => item.Level.ToString()),
                    new TextElement(() => School(item)),
                    new TextElement(() => RecLabel(item)),
                }, rowTooltip: () => item.TooltipTemplate()); // Space anywhere in the row → spell detail
            }
            _selectorPanel.Add(table);
        }

        // ----- the known-spells reference (mirrors the game's spellbook panel) -----

        private void TickKnown()
        {
            if (_knownSection == null) return;
            var sb = Phase.SpellbookVM;
            if (sb == null || sb.CurrentSpellbook?.Value == null) return; // spellbook not built yet

            // Rebuild the level radio only when the set of levels-with-spells changes (rare — happens
            // when picking a spell adds a previously-empty level). Focus is in the picker then, not here.
            var levels = AvailableLevels().ToList();
            string lsig = string.Join(",", levels.Select(e => e.SpellbookLevel.Level));
            if (lsig != _levelsSig)
            {
                _levelsSig = lsig;
                _spellsSig = null;
                BuildKnownLevels(levels);
            }

            // Rebuild the spell list when the selected level (or its contents) changes. The known-spells
            // VM refreshes on LateUpdate, so this lands a frame after a level pick — fine, focus is on
            // the radio, and the list is its sibling.
            string ssig = KnownSig();
            if (ssig != _spellsSig)
            {
                _spellsSig = ssig;
                FillKnownList();
            }
        }

        private void BuildKnownLevels(List<SpellbookLevelSwitcherEntityVM> levels)
        {
            _knownSection.Clear();
            _knownList = null;
            if (levels.Count == 0)
            {
                _knownSection.Add(new TextElement("No spells known yet."));
                return;
            }

            var radio = new ListContainer("Spell level");
            foreach (var e in levels)
            {
                var ent = e; // capture
                radio.Add(new ProxySelectionItem(ent, () => LevelName(ent.SpellbookLevel.Level)));
            }
            _knownSection.Add(radio);

            _knownList = new Panel();
            _knownSection.Add(_knownList);
        }

        private void FillKnownList()
        {
            if (_knownList == null) return;
            _knownList.Clear();
            var known = Phase.SpellbookVM?.SpellbookKnownSpellsVM?.KnownSpells;
            if (known == null || known.Count == 0)
            {
                _knownList.Add(new TextElement("No spells known at this level."));
                return;
            }
            var list = new ListContainer();
            foreach (var vm in known)
            {
                var v = vm; // capture
                list.Add(new TextElement(v.DisplayName, tooltip: () => v.Tooltip));
            }
            _knownList.Add(list);
        }

        private IEnumerable<SpellbookLevelSwitcherEntityVM> AvailableLevels()
        {
            var sw = Phase.SpellbookVM?.SpellbookLevelSwitcherVM;
            var entities = sw?.SelectionGroup?.EntitiesCollection;
            if (entities == null) return Enumerable.Empty<SpellbookLevelSwitcherEntityVM>();
            return entities.Where(e => e != null && e.IsAvailable.Value); // only levels with spells
        }

        private string KnownSig()
        {
            var sb = Phase.SpellbookVM;
            int lvl = sb?.CurrentSpellbookLevel?.Value?.Level ?? -1;
            var known = sb?.SpellbookKnownSpellsVM?.KnownSpells;
            int count = known?.Count ?? 0;
            string first = count > 0 ? known[0].DisplayName : "";
            return lvl + ":" + count + ":" + first;
        }

        // ----- shared cell text -----

        private static string LevelName(int level)
            => level == 0 ? "Cantrips"
             : level == 11 ? "Favorites"
             : level < Words.Length ? Words[level] + " level"
             : level + " level";

        private static string School(CharGenSpellSelectorItemVM v)
            => v.HasInSpellbook ? v.SchoolName + " / " + UIStrings.Instance.Tooltips.KnownSpell : v.SchoolName;

        private static string RecLabel(CharGenSpellSelectorItemVM v)
        {
            switch (v.Recommendation.Recommendation.Value)
            {
                case RecommendationType.Recommended: return "recommended";
                case RecommendationType.NotRecommended: return "not recommended";
                default: return ""; // Neutral shows no marker
            }
        }
    }
}
