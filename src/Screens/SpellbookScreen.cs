using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (spellbook labels)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // SpellbookVM
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
        public override string ScreenName => "Spellbook";
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Spellbook;

        private Container _content;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel;

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
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
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static SpellbookVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.SpellbookVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        // Refills when the viewed unit, the active spellbook, the selected level, or the known-spell set changes.
        private static string Sig(SpellbookVM vm)
        {
            var sb = new StringBuilder();
            sb.Append(Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value?.CharacterName).Append('|');
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
                var sw = new ListContainer("Characters");
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
            _content.Clear();

            if (!vm.HasSpellbooks.Value)
            {
                _content.Add(new TextElement("This character has no spells."));
                RestoreFocus(cap);
                return;
            }

            // Spellbook (caster) switcher — only when there's more than one.
            var books = vm.SpellbookSwitcherVM;
            if (books != null && books.HasMoreThanOneSpellbooks.Value && books.SelectionGroup?.EntitiesCollection != null)
            {
                var sheet = new FlowSheet();
                var bar = sheet.Bar("Spellbooks");
                foreach (var e in books.SelectionGroup.EntitiesCollection) { var ent = e; bar.Cell(new ProxySelectionItem(ent, () => ent.BookName)); }
                sheet.Reflow();
                _content.Add(sheet);
            }

            // Caster characteristics.
            var ch = vm.SpellbookCharacteristicsVM;
            if (ch != null)
            {
                var sink = new FlowSheetCharSheetSink();
                var g = new StatGroup("Caster");
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
                var bar = sheet.Bar("Spell level");
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
            RestoreFocus(cap);
        }

        // The memorizing panel (prepared casters): the special (domain/favorite) and common memorized slots
        // for the current level — Enter on a filled slot forgets it; empty slots are filled from the known
        // list — plus a spells-per-day / status readout.
        private void BuildMemorize(SpellbookVM vm)
        {
            var panel = vm.SpellbookMemorizingPanelVM;
            if (panel == null) return;
            var sheet = new FlowSheet(WithLevel("Memorize", vm));

            if (panel.IsCorrectLevelValue && panel.HasAnySlot)
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

            var info = sheet.List("Spell slots");
            info.Item(new TextElement(() => Message.Localized("ui", "spellbook.spells_per_day").Resolve() + ": " + panel.SpellsPerDay));
            if (panel.IsSpontaneous)
                info.Item(new TextElement(() => Message.Localized("ui", "spellbook.remaining").Resolve() + ": " + panel.RemainingSpontaneousSpells));
            if (panel.NeedToSleep) info.Item(new TextElement((string)UIStrings.Instance.SpellBookTexts.NeedToSleep));

            sheet.Reflow();
            if (sheet.RowCount > 0) _content.Add(sheet);
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
            var sheet = new FlowSheet(WithLevel("Spells", vm));
            var t = sheet.Table("Spells", "School");
            bool any = false;
            if (known != null)
                foreach (var spell in known)
                {
                    if (spell == null) continue;
                    any = true;
                    var s = spell;
                    t.Row(new ProxyKnownSpell(s, unit), new UIElement[] { new TextElement(() => s.SchoolName) });
                }
            if (!any) t.Row(new TextElement("No spells at this level."), new UIElement[0]);
            t.Associate(0);
            sheet.Reflow();
            _content.Add(sheet);
        }

        private static string LevelName(int level) => level == 0 ? "Cantrips" : "Level " + level;

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
