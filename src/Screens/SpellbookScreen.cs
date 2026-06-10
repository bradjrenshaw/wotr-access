using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Spells; // CantripsType
using Kingmaker.Blueprints.Root.Strings; // UIStrings (spellbook labels)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // SpellbookVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.KnownSpells; // AbilityDataVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MemorizingPanel; // SpellbookMemorizingPanelVM, SpellbookMemorizeSlotVM
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The spellbook service window (<see cref="SpellbookVM"/>). Page-like content: a character switcher,
    /// the caster characteristics (caster level / concentration / penetration / failure), the spellbook
    /// switcher (multiclass casters), the spell-level switcher, and the known spells for the current level
    /// as an associated-element table (the spell is the row's element; School is a column; Enter adds it to
    /// the action bar). Content refills when the unit / spellbook / level / spell set changes, restoring the
    /// cursor by grid position. Memorizing (prepared casters), casting from the book, and the metamagic /
    /// magic-hack builders are later slices. Escape closes.
    /// </summary>
    public sealed class SpellbookScreen : Screen
    {
        public override string Key => "service.Spellbook";
        public override string ScreenName => Loc.T("screen.spellbook");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Spellbook;

        private Container _content;
        private bool _built;
        private bool _wasMixer;
        private string _sig;
        private string _lastRestoreLabel;

        public override void OnPush() { _built = false; _wasMixer = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell(vm);
            var sig = Sig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // In the metamagic builder, Back leaves the builder (not the whole window).
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                var vm = Vm();
                if (vm != null && vm.MetamagicBuilderMode.Value) vm.MetamagicBuilderMode.Value = false;
                else ServiceWindows()?.HandleCloseAll();
            });
        }

        private static bool MixerActive(SpellbookVM vm)
            => vm.MetamagicBuilderMode.Value && vm.SpellbookMetamagicMixerVM != null;

        private static SpellbookVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.SpellbookVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        // Refills when the viewed unit, the active spellbook, the selected level, or the known-spell set changes.
        private static string Sig(SpellbookVM vm)
        {
            var sb = new StringBuilder();
            sb.Append(Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value?.CharacterName).Append('|');

            // Metamagic builder mode: a wholly different content set. Refresh on the applied set / result level.
            if (MixerActive(vm))
            {
                sb.Append("MIX|").Append(vm.CurrentSelectedSpell?.Value?.DisplayName).Append('|');
                var sel = vm.SpellbookMetamagicMixerVM.SpellbookMetamagicSelector;
                if (sel?.MetamagicItems != null)
                    foreach (var i in sel.MetamagicItems) if (i != null) sb.Append(i.Feature?.Name).Append(i.IsSelected ? '+' : '-').Append(',');
                sb.Append('|').Append(sel?.SpellbookSpellLevelSelector?.ResultSpellLevel.Value ?? -1);
                sb.Append('|').Append(vm.SpellbookMetamagicMixerVM.CanWriteSpell.Value);
                return sb.ToString();
            }

            sb.Append(vm.CurrentSpellbook?.Value?.Blueprint?.name).Append('|');
            sb.Append(vm.CurrentSpellbookLevel?.Value?.Level ?? -1).Append('|');
            var known = vm.SpellbookKnownSpellsVM?.KnownSpells;
            if (known != null) foreach (var s in known) if (s != null) sb.Append(s.DisplayName).Append(',');
            // Memorized slots fill/empty as you memorize/forget — fold them in so that triggers a refresh.
            var panel = vm.SpellbookMemorizingPanelVM;
            if (panel != null) { AppendMem(sb, panel.CommonMemorizedSpells); AppendMem(sb, panel.SpecialMemorizedSpells); }
            return sb.ToString();
        }

        private static void AppendMem(StringBuilder sb, List<SpellbookMemorizeSlotVM> slots)
        {
            sb.Append('|');
            if (slots == null) return;
            foreach (var s in slots) sb.Append(s?.SpellData != null ? s.DisplayName : "-").Append(',');
        }

        private void BuildShell(SpellbookVM vm)
        {
            _built = true;
            Clear();

            var party = Game.Instance?.Player?.Party;
            if (party != null && party.Count > 0)
            {
                var sw = new ListContainer(Loc.T("label.characters"));
                foreach (var u in party)
                {
                    var unit = u;
                    sw.Add(new ProxyActionButton(() => unit.CharacterName, () => true,
                        () => Game.Instance.SelectionCharacter.SetSelected(unit), actionVerb: "select"));
                }
                Add(sw);
            }

            _content = new Panel();
            Add(_content);
            Navigation.Attach(this);
        }

        private void RefillContent(SpellbookVM vm)
        {
            if (_content == null) return;
            var cap = CaptureFocus();
            // Entering/leaving the metamagic builder is a full content swap (and the context menu intervened),
            // so a position restore is meaningless — drop focus onto the first cell instead.
            bool mixer = MixerActive(vm);
            bool modeChanged = mixer != _wasMixer;
            _wasMixer = mixer;
            _content.Clear();

            if (!vm.HasSpellbooks.Value)
            {
                _content.Add(new TextElement(() => Loc.T("spell.none")));
                Settle(cap, modeChanged);
                return;
            }

            if (mixer) { BuildMixer(vm); Settle(cap, modeChanged); return; }

            // Spellbook (caster) switcher — only when there's more than one.
            var books = vm.SpellbookSwitcherVM;
            if (books != null && books.HasMoreThanOneSpellbooks.Value && books.SelectionGroup?.EntitiesCollection != null)
            {
                var sheet = new FlowSheet();
                var bar = sheet.Bar(Loc.T("spell.spellbooks"));
                foreach (var e in books.SelectionGroup.EntitiesCollection) { var ent = e; bar.Cell(new ProxySelectionItem(ent, () => ent.BookName)); }
                sheet.Reflow();
                _content.Add(sheet);
            }

            // Caster characteristics.
            var ch = vm.SpellbookCharacteristicsVM;
            if (ch != null)
            {
                var sink = new FlowSheetCharSheetSink();
                var g = new StatGroup(Loc.T("spell.caster"));
                g.Row(CharInfoStatRows.Value(ch.CasterLevel, signed: false));
                g.Row(CharInfoStatRows.Value(ch.Concentration, signed: true));
                g.Row(CharInfoStatRows.Value(ch.SpellPenetration, signed: true));
                g.Row(CharInfoStatRows.Value(ch.SpellFailureChance, signed: false));
                sink.StatGroup(g);
                if (sink.Build() is FlowSheet cs && cs.RowCount > 0) _content.Add(cs);
            }

            // Spell-level switcher (only levels that have spells).
            var levels = vm.SpellbookLevelSwitcherVM?.SelectionGroup?.EntitiesCollection;
            if (levels != null)
            {
                var sheet = new FlowSheet();
                var bar = sheet.Bar(Loc.T("metamagic.spell_level"));
                foreach (var e in levels)
                {
                    if (e == null || !e.IsAvailable.Value) continue;
                    var ent = e;
                    bar.Cell(new ProxySelectionItem(ent, () => LevelName(ent.SpellbookLevel.Level)));
                }
                sheet.Reflow();
                _content.Add(sheet);
            }

            BuildKnownSpells(vm);
            BuildMemorize(vm);
            Settle(cap, modeChanged);
        }

        private void Settle((int child, int row, int col) cap, bool modeChanged)
        {
            if (modeChanged) FocusFirstCell();
            else RestoreFocus(cap);
        }

        // Drop focus onto the first cell of the first non-empty FlowSheet (used when the content mode flips so
        // there's no meaningful prior position to restore) — so the grid is entered, not just labelled.
        private void FocusFirstCell()
        {
            foreach (var ch in _content.Children)
            {
                if (ch is FlowSheet fs && fs.RowCount > 0)
                {
                    int c = fs.LeftmostVisitable(0);
                    var cell = c >= 0 ? fs.CellAt(0, c) : null;
                    if (cell != null) { _lastRestoreLabel = cell.GetLabelText(); Navigation.Focus(cell, announce: true); return; }
                }
            }
            var f = _content.FirstFocusable();
            if (f != null) { _lastRestoreLabel = f.GetLabelText(); Navigation.Focus(f, announce: true); }
        }

        // The memorizing panel (prepared casters): the special (domain/favorite) and common memorized slots
        // for the current level — Enter on a filled slot forgets it; empty slots are filled from the known
        // list — plus a spells-per-day / status readout.
        private void BuildMemorize(SpellbookVM vm)
        {
            var panel = vm.SpellbookMemorizingPanelVM;
            if (panel == null) return;
            var sheet = new FlowSheet(WithLevel(Loc.T("spell.memorize"), vm));

            // Like the game: show the memorize slots when there are any at this level, else a substitute
            // line (spontaneous spells-per-day, cantrips "cast at will", not-enough-stat/level, …).
            bool hasSlots = panel.IsCorrectLevelValue && panel.HasAnySlot;
            if (hasSlots)
            {
                if (panel.HasSpecialSlots && panel.SpecialMemorizedSpells != null)
                {
                    var r = sheet.List(SpecialLabel(panel));
                    foreach (var s in panel.SpecialMemorizedSpells) if (s != null) r.Item(new ProxyMemorizeSlot(s));
                }
                if (panel.HasCommonSlots && panel.CommonMemorizedSpells != null)
                {
                    var r = sheet.List((string)UIStrings.Instance.SpellBookTexts.MemorizedSpells);
                    foreach (var s in panel.CommonMemorizedSpells) if (s != null) r.Item(new ProxyMemorizeSlot(s));
                }
            }

            var info = new List<UIElement>();
            if (!hasSlots) info.Add(new TextElement(() => SubstituteText(panel))); // live (spontaneous count changes on cast)
            if (panel.NeedToSleep) info.Add(new TextElement((string)UIStrings.Instance.SpellBookTexts.NeedToSleep));
            if (info.Count > 0) { var r = sheet.List(Loc.T("spell.slots")); foreach (var e in info) r.Item(e); }

            sheet.Reflow();
            if (sheet.RowCount > 0) _content.Add(sheet);
        }

        // Mirrors SpellbookMemorizingPanelView.SetupSubstituteText: the line shown in place of slots — the
        // spontaneous daily count, the cantrip/orison "cast at will" note, or a can't-cast/no-slots reason.
        private static string SubstituteText(SpellbookMemorizingPanelVM p)
        {
            var t = UIStrings.Instance.SpellBookTexts;
            if (p.IsCorrectLevelValue && !p.IsCantripLevel)
            {
                if (p.IsSpontaneous)
                {
                    if (p.NotEnoughStat) return string.Format((string)t.NotEnoughAbilityScore, p.CasterStat, p.CasterStatMin);
                    if (p.HasAnyKnownSpells) return string.Format((string)t.SpontaneuseSpellsPreDay, p.RemainingSpontaneousSpells, p.SpellsPerDay);
                    return (string)t.CanNotCastSpellsOfLevel;
                }
                if (!p.HasCommonSlots && !p.HasSpecialSlots)
                {
                    if (p.NotEnoughStat) return string.Format((string)t.NotEnoughAbilityScore, p.CasterStat, p.CasterStatMin);
                    if (p.NotEnoughLevel) return (string)t.CanNotCastSpellsOfLevel;
                    return (string)t.CharacterHasNotSlots;
                }
                return (string)t.CanNotCastSpellsOfLevel;
            }
            if (p.IsCorrectLevelValue && p.IsCantripLevel)
            {
                if (!p.HasCantrips) return (string)t.CanNotCastSpellsOfLevel;
                return p.CantripsType == CantripsType.Orisions ? (string)t.MemorizePanelOrisons : (string)t.MemorizePanelCantrips;
            }
            return (string)t.DontHaveSpellsInBook;
        }

        // The metamagic builder (entered via a known spell's "Apply metamagic"): the base spell, the known
        // metamagic feats as toggles, the Heighten level stepper (when applicable), the resulting spell
        // (name + level + whether it's castable), and "Write spell" — which creates the metamagic'd custom
        // spell and leaves the builder. Back leaves the builder.
        private void BuildMixer(SpellbookVM vm)
        {
            var mixer = vm.SpellbookMetamagicMixerVM;
            var sel = mixer.SpellbookMetamagicSelector;
            var baseSpell = vm.CurrentSelectedSpell?.Value;
            var sheet = new FlowSheet(Loc.T("spell.metamagic"));

            if (baseSpell != null)
            {
                var bs = baseSpell;
                sheet.List(Loc.T("spell.spell")).Item(new TextElement(() => bs.DisplayName, null, () => bs.Tooltip));
            }

            var feats = sel?.MetamagicItems;
            if (feats != null && feats.Count > 0)
            {
                var r = sheet.List(Loc.T("spell.metamagic"));
                foreach (var item in feats) if (item != null) r.Item(new ProxyMetamagicToggle(item));
            }
            else
            {
                sheet.List(Loc.T("spell.metamagic")).Item(new TextElement(() => Loc.T("spell.no_metamagic")));
            }

            var lvl = sel?.SpellbookSpellLevelSelector;
            if (lvl != null && lvl.CanChangeLevel.Value)
                sheet.List(Loc.T("spell.heighten")).Item(new ProxyMetamagicLevel(lvl));

            var result = sheet.List(Loc.T("metamagic.result"));
            int resultLevel = lvl?.ResultSpellLevel.Value ?? 0;
            bool castable = lvl?.CanUseSpell ?? true;
            result.Item(new TextElement(ResultLine(baseSpell, resultLevel, castable)));
            // Always show Write (greyed when you can't write yet — no metamagic applied, or the result level
            // exceeds your castable slots), mirroring the game's Interactable = CanWriteSpell button.
            result.Item(new ProxyActionButton(() => Message.Localized("ui", "metamagic.write").Resolve(),
                () => mixer.CanWriteSpell.Value, () => mixer.TryWriteNewSpell()));

            sheet.Reflow();
            _content.Add(sheet);
        }

        private static string ResultLine(AbilityDataVM baseSpell, int level, bool castable)
        {
            var s = Loc.T("metamagic.result_line", new { name = baseSpell?.DisplayName ?? "", level = LevelName(level) });
            if (!castable) s += " (" + Loc.T("metamagic.too_high") + ")";
            return s;
        }

        // The special-slots heading: an explicit name if the book sets one, else Domain / Favorite school.
        private static string SpecialLabel(SpellbookMemorizingPanelVM panel)
        {
            if (!string.IsNullOrEmpty(panel.SpecialSlotsName)) return panel.SpecialSlotsName;
            return panel.HasDomainSlots
                ? (string)UIStrings.Instance.SpellBookTexts.DomainSlots
                : (string)UIStrings.Instance.SpellBookTexts.FavoriteSchoolSlots;
        }

        // The known spells at the current level as an associated-element table: column 0 is the spell (name +
        // tooltip + add-to-bar), School is a value column.
        private void BuildKnownSpells(SpellbookVM vm)
        {
            var known = vm.SpellbookKnownSpellsVM?.KnownSpells;
            var unit = vm.UnitDescriptor?.Value;
            var sheet = new FlowSheet(WithLevel(Loc.T("spell.spells"), vm));
            var t = sheet.Table(Loc.T("spell.spells"), Loc.T("col.school"));
            bool any = false;
            if (known != null)
                foreach (var spell in known)
                {
                    if (spell == null) continue;
                    any = true;
                    var s = spell;
                    t.Row(new ProxyKnownSpell(s, unit), new UIElement[] { new TextElement(() => s.SchoolName) });
                }
            if (!any) t.Row(new TextElement(() => Loc.T("spell.none_at_level")), new UIElement[0]);
            t.Associate(0);
            sheet.Reflow();
            _content.Add(sheet);
        }

        private static string LevelName(int level)
            => level == 0 ? Loc.T("spell.cantrips") : Loc.T("spell.level", new { level });

        // The memorize/known sections are filtered to the current spell level (the level switcher drives both),
        // so put the level in their headers — otherwise it's only known from the switcher region.
        private static string WithLevel(string label, SpellbookVM vm)
        {
            int lvl = vm.CurrentSpellbookLevel?.Value?.Level ?? -1;
            return lvl < 0 ? label : label + ", " + LevelName(lvl);
        }

        // (contentChildIndex, row, col) of the focused cell, or child = -1 when focus is outside the content.
        private (int child, int row, int col) CaptureFocus()
        {
            var cur = Navigation.Active?.Current;
            if (cur != null)
                for (int i = 0; i < _content.Children.Count; i++)
                    if (_content.Children[i] is FlowSheet fs && fs.TryCoords(cur, out int r, out int c))
                        return (i, r, c);
            return (-1, 0, 0);
        }

        private void RestoreFocus((int child, int row, int col) cap)
        {
            if (cap.child < 0) return;
            UIElement cell = null;
            if (cap.child < _content.Children.Count && _content.Children[cap.child] is FlowSheet fs && fs.RowCount > 0)
            {
                int r = System.Math.Min(cap.row, fs.RowCount - 1);
                int c = fs.Visitable(r, cap.col) ? cap.col : fs.LeftmostVisitable(r);
                if (c >= 0) cell = fs.CellAt(r, c);
            }
            cell = cell ?? _content.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }
    }
}
