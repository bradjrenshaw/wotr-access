using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings.FormationTexts (game-localized labels)
using Kingmaker.UI.MVVM._VM.Formation; // FormationVM
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary ("Hold the line")
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The party-formation window (<see cref="FormationVM"/> on <c>InGameStaticPartVM.FormationVM</c>),
    /// opened from the HUD menu's Formation button. Tab stops, in order: the <b>formations list</b> (a radio
    /// of the 6 — Optimal Auto first, then the editable named ones), [the editing FIELD — added next], then
    /// <b>Restore to default</b>, the <b>Hold the line</b> preserve-formation toggle, and <b>Close</b>. The
    /// list drives the game's own SelectionGroupRadioVM (via ProxySelectionItem); Restore/Hold only apply to
    /// an editable (Custom) formation, so they grey out on the Auto one. Layer 16, Exclusive (owns the keyboard
    /// while open). Back / Escape closes via FormationVM.Close.
    /// </summary>
    public sealed class FormationScreen : Screen
    {
        public FormationScreen() { Wrap = true; }

        public override string Key => "overlay.formation";
        public override string ScreenName => Loc.T("screen.formation");
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override bool AllowsTypeahead => false; // WASD drive the editor field; no name-search needed

        // While the WASD editor field is focused, claim the Formation category so WASD move the cursor; on the
        // other tab stops only UI is live (so WASD stay free). Mirrors the in-game screen's focus-driven flip.
        private static readonly WrathAccess.Input.InputCategory[] FieldCats =
            { WrathAccess.Input.InputCategory.Formation, WrathAccess.Input.InputCategory.UI };
        private static readonly WrathAccess.Input.InputCategory[] BaseCats =
            { WrathAccess.Input.InputCategory.UI };
        public override System.Collections.Generic.IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories
            => Navigation.Current is FormationField ? FieldCats : BaseCats;

        public override bool IsActive() => Vm() != null;

        internal static FormationVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.FormationVM?.Value;

        private FormationVM _built;

        public override void OnPush() { _built = null; Rebuild(); }
        public override void OnPop() { Clear(); _built = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm != null && vm != _built)
            {
                Rebuild();
                Navigation.Attach(this);
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _built = vm;
            if (vm == null) return;

            // The formations, in the game's order (Optimal Auto, then the five editable named shapes). Each
            // radio item is the game's FormationSelectionItemVM, so ProxySelectionItem's select contract
            // (IsSelected / SetSelectedFromView) drives the real SelectionGroupRadioVM → CurrentFormationIndex.
            // Names come from the formation blueprints (game-localized, passed through), keyed by item index.
            var names = FormationNames();
            var formations = new ListContainer(Loc.T("formation.list"));
            foreach (var item in vm.FormationSelector.EntitiesCollection)
            {
                var it = item; // capture per-iteration for the closure
                formations.Add(new ProxySelectionItem(it,
                    () => it.FormationIndex >= 0 && it.FormationIndex < names.Count ? names[it.FormationIndex] : ""));
            }
            Add(formations);

            // The WASD editing field (move the cursor, pick up / drop members). Editing applies to a Custom
            // formation; on Auto it reads the layout but reports it can't be edited.
            Add(new FormationField());

            // Footer. Restore + Hold the line act on the Custom formation only (the game greys them on Auto);
            // their labels are the game's own localized strings.
            var t = UIStrings.Instance.FormationTexts;
            Add(new ProxyActionButton(() => (string)t.RestoreToDefault,
                () => Vm()?.IsCustomFormation ?? false, () => Vm()?.ResetCurrentFormation()));
            Add(new ProxyBoolToggle((string)t.HoldTheLine,
                () => Vm()?.IsPreserveFormation.Value ?? false, () => Vm()?.SwitchPreserveFormation(),
                () => Vm()?.IsCustomFormation ?? false,
                tooltip: () => new TooltipTemplateGlossary("HoldTheLine"))); // same glossary entry the game shows
            Add(new ProxyActionButton(() => Loc.T("action.close"), () => true, () => Vm()?.Close()));
        }

        // The predefined formations' display names, in order (parallel to the selector items by index).
        private static List<string> FormationNames()
        {
            var list = new List<string>();
            var formations = Game.Instance?.BlueprintRoot?.Formations?.PredefinedFormations;
            if (formations != null)
                foreach (var f in formations) list.Add(f != null ? (string)f.Name : "");
            return list;
        }
    }
}
