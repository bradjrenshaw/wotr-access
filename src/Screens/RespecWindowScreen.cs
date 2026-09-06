using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (the window's own labels)
using Kingmaker.UI.MVVM._VM.CharGen;    // RespecWindowVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The RETRAINING progress window (<see cref="RespecWindowVM"/>, a static singleton the respec
    /// flow keeps up between level-up passes; the same VM backs the plain "Level up" window when
    /// <c>IsRespec</c> is false). After the retrain picker's Yes, the character is reset and this
    /// window shows "Level 2/3" / "Mythic rank 0/0" with a Complete button: each level-up press
    /// opens the level-up wizard (which the mod already covers), the window re-shows with the new
    /// numbers, and Complete — enabled only once nothing is left to take — ends the flow. It was a
    /// silent modal that blocked the game after a retrain (tester repro). Hidden while the wizard
    /// is up so it never shadows the chargen screen. Layer 18: above the retrain picker (17) it
    /// sits on. No close: the sighted window has no X either — Complete is the only way out.
    /// </summary>
    public sealed class RespecWindowScreen : Screen
    {
        public override string Key => "overlay.respec";
        public override int Layer => 18;

        public override string ScreenName
        {
            get
            {
                var vm = RespecWindowVM.Instance;
                var s = vm != null && !vm.IsRespec ? UIStrings.Instance?.CharGen?.LevelUp : UIStrings.Instance?.CharGen?.RespecTitle;
                return TextUtil.StripRichText(s) ?? Loc.T("screen.respec");
            }
        }

        // The wizard this window launches replaces it on screen; while the in-game chargen VM is
        // live the window must yield (the mod's CharGenScreen owns the keyboard then).
        public override bool IsActive()
        {
            if (RespecWindowVM.Instance == null) return false;
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.CharGenContextVM?.CharGenVM?.Value == null;
        }

        public override IEnumerable<ElementAction> GetActions() { yield break; }

        public override void Build(GraphBuilder b)
        {
            var vm = RespecWindowVM.Instance;
            if (vm == null) return;
            string k = "respec:" + vm.GetHashCode() + ":";
            var cs = UIStrings.Instance?.CharacterSheet;
            var cg = UIStrings.Instance?.CharGen;

            b.BeginStop(k + "main").PushContext(ScreenName, "list");
            b.AddItem(ControlId.Structural(k + "name"),
                GraphNodes.Text(() => vm.CurrentUnit.Value?.CharacterName ?? ""));

            // "Level 2/3" — the row IS the level-up button (the sighted row's plus icon).
            b.AddItem(ControlId.Structural(k + "level"), GraphNodes.Button(
                () => TextUtil.StripRichText(cs?.LEVEL) + " " + vm.CurrentCharacterLevel.Value + "/" + vm.EndLevel.Value,
                () => vm.InitiateNextLevelup(),
                () => vm.CanUpCharacterLevel.Value));
            if (vm.CanUpMaxCharacterLevel.Value)
                b.AddItem(ControlId.Structural(k + "maxlevel"), GraphNodes.Button(
                    () => Loc.T("respec.max_level"), () => vm.MaxLevelup(), () => vm.CanUpCharacterLevel.Value));

            // "Mythic rank 0/0" — the mythic-up button; blocked (with the game's reason) while the
            // character level is below the next rank's requirement.
            b.AddItem(ControlId.Structural(k + "mythic"), GraphNodes.Button(
                () => TextUtil.StripRichText(string.Format(cs?.MythicLevel ?? "{0}",
                    vm.CurrentMythicLevel.Value + "/" + vm.EndMythicLevel.Value)),
                () => vm.InitiateNextMythic(),
                () => vm.CanUpMythicLevel.Value && !vm.HasMythicLevelRestriction.Value));
            if (vm.HasMythicLevelRestriction.Value)
                b.AddItem(ControlId.Structural(k + "mythicrestriction"), GraphNodes.Text(
                    () => TextUtil.StripRichText(string.Format(cg?.RespecRestrictionMainLevel ?? "{0}", vm.MythicRestrictionLevel.Value))));
            if (vm.CanUpMaxMythicLevel.Value)
                b.AddItem(ControlId.Structural(k + "maxmythic"), GraphNodes.Button(
                    () => Loc.T("respec.max_mythic"), () => vm.MaxMythic(),
                    () => vm.CanUpMythicLevel.Value && !vm.HasMythicLevelRestriction.Value));

            // Complete: enabled once nothing is left to take (IsFinished), like the sighted button.
            b.AddItem(ControlId.Structural(k + "complete"), GraphNodes.Button(
                () => TextUtil.StripRichText(cg?.Complete) ?? Loc.T("respec.complete"),
                () => vm.Complete(),
                () => vm.IsFinished.Value));
            b.PopContext();
        }
    }
}
