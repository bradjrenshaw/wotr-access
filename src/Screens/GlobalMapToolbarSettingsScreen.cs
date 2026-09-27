using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.MVVM._VM.GlobalMap.Toolbar; // GlobalMapToolbarSettingsVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world-map toolbar's SETTINGS popup (the gear: a small panel that fades in beside the strip
    /// with three switches and a close button; Escape closes it too) as its own modal screen, pushed
    /// the moment the game creates the panel's VM and popped when it disposes. One tab stop holds
    /// the switches, another the Close button. Each switch is the VM entity the panel's checkbox is
    /// bound to; Close and Escape call the same VM Close the panel's button and Esc handler do.
    /// </summary>
    public sealed class GlobalMapToolbarSettingsScreen : Screen
    {
        public GlobalMapToolbarSettingsScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_toolbar_settings";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.ToolbarSettingsTitle;
        public override int Layer => 15; // a modal over the world-map base context, like the location panel

        private static GlobalMapToolbarSettingsVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapToolbarVM?.SettingsVM.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            if (Vm() == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;
            b.BeginStop("switches").PushContext(ScreenName, "group");
            b.AddItem(ControlId.Structural("tbs:name"), GraphNodes.Toggle(
                () => ct.ToolbarSettingsShowLocationNameTitle,
                () => Vm()?.ShowLocationName.IsOn.Value ?? false,
                () => Vm()?.ShowLocationName.Switch()));
            b.AddItem(ControlId.Structural("tbs:auto"), GraphNodes.Toggle(
                () => ct.ToolbarSettingsAutoTacticalCombatTitle,
                () => Vm()?.AutoTacticalCombat.IsOn.Value ?? false,
                () => Vm()?.AutoTacticalCombat.Switch()));
            b.AddItem(ControlId.Structural("tbs:demons"), GraphNodes.Toggle(
                () => ct.ToolbarSettingsShowDemonArmiesTravelTitle,
                () => Vm()?.ShowDemonArmiesTravel.IsOn.Value ?? false,
                () => Vm()?.ShowDemonArmiesTravel.Switch()));
            b.PopContext();

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("tbs:close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }
    }
}
