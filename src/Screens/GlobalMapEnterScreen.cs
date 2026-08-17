using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Globalmap.Blueprints; // BlueprintGlobalMapPoint
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapEnterMessageVM
using UnityEngine;
using WrathAccess.Exploration; // GlobalMapEnterPanel
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map's LOCATION PANEL (the game's GlobalMapEnterMessageVM popup: description +
    /// Enter/travel + Close) as its OWN modal screen — it used to be a tab stop inside the map
    /// screen, which meant Tab-hunting for it after selecting a location (user: "nonintuitive").
    /// Now it pushes the moment the game raises the panel (Enter on a location), announces itself
    /// as the location's name, and Up/Down walks lore → description → actions directly; Escape (or
    /// Close) dismisses back to the map. The VM churns (dispose+recreate for the same location), so
    /// state is keyed on the LOCATION with a short grace absorbing transient nulls, texts captured
    /// at open, and the ACTIONS resolving the live VM each press.
    /// </summary>
    public sealed class GlobalMapEnterScreen : Screen
    {
        public GlobalMapEnterScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_location";
        public override string ScreenName => _title ?? Loc.T("worldmap.panel");
        public override int Layer => 15; // a modal over the world-map base context (like the encounter popup)

        // Grace-stabilized panel state (see class doc).
        private static BlueprintGlobalMapPoint _loc;
        private static float _clearAt;
        private static string _title, _lore, _desc, _acceptLabel, _manageLabel, _closeLabel;
        private static bool _acceptEnabled, _hasSettlement;

        /// <summary>The panel is up (grace-stable) — the world-map cursor and sonar freeze on this.</summary>
        public static bool PanelActive => _loc != null;

        public override bool IsActive()
        {
            Sync();
            return _loc != null;
        }

        private static GlobalMapEnterMessageVM PanelVm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapEnterMessageVM?.Value;
        }

        private static void Sync()
        {
            var vm = PanelVm();
            var loc = vm != null && vm.Location != null ? vm.Location.Blueprint : null;
            if (loc != null)
            {
                _clearAt = 0f;
                if (loc != _loc) Open(loc, vm);
                return;
            }
            if (_loc == null) return;
            if (_clearAt == 0f) _clearAt = Time.unscaledTime + 0.25f;
            else if (Time.unscaledTime >= _clearAt) Clear();
        }

        private static void Clear() { _loc = null; _clearAt = 0f; _title = null; }

        private static void Open(BlueprintGlobalMapPoint loc, GlobalMapEnterMessageVM vm)
        {
            _loc = loc;
            _clearAt = 0f;
            // Location lore first (what the place is), then the game-panel body (travel time / enter
            // confirmation / closed or restricted reason). Location-stable, captured here; the screen
            // announces itself as the location's name when it takes focus.
            _title = TextUtil.StripRichText(GlobalMapEnterPanel.Title(vm));
            _lore = GlobalMapEnterPanel.LocationDescription(vm);
            GlobalMapEnterPanel.Compute(vm, out _desc, out _acceptEnabled);
            _acceptLabel = TextUtil.StripRichText(GlobalMapEnterPanel.AcceptLabel(vm));
            _hasSettlement = GlobalMapEnterPanel.HasSettlement(vm);
            _manageLabel = TextUtil.StripRichText(GlobalMapEnterPanel.ManageLabel());
            _closeLabel = TextUtil.StripRichText(GlobalMapEnterPanel.CloseLabel());
        }

        public override void OnPop() => Clear();

        public override void Build(GraphBuilder b)
        {
            if (_loc == null) return;
            string k = "panel:" + _loc.GetHashCode() + ":"; // re-keys per location
            b.BeginStop("panel");

            if (!string.IsNullOrWhiteSpace(_lore))
                b.AddItem(ControlId.Structural(k + "lore"), GraphNodes.Text(() => _lore));
            if (!string.IsNullOrWhiteSpace(_desc))
                b.AddItem(ControlId.Structural(k + "desc"), GraphNodes.Text(() => TextUtil.StripRichText(_desc)));

            // Each action fires the game's button-click sound + the VM method the real OwlcatButton is
            // wired to (Accept/AlternativeAction/Close) — same behavior as pressing the button (see
            // GlobalMapEnterMessagePCView), resolving the LIVE VM each press, never a stale capture.
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                () => _acceptLabel, AcceptLive, () => _acceptEnabled, sound: null));
            if (_hasSettlement)
                b.AddItem(ControlId.Structural(k + "manage"), GraphNodes.Button(
                    () => _manageLabel, () => { PlayClick(); PanelVm()?.AlternativeAction(); }, sound: null));
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(
                () => _closeLabel, () => { PlayClick(); PanelVm()?.Close(); }, sound: null));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => { PlayClick(); PanelVm()?.Close(); });
        }

        // The default button-click sound the OwlcatButton plays on a left-click (UISoundController, exactly as
        // the game does it), so our VM-driven actions sound identical to a real button press.
        private static void PlayClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();

        // Travel / Enter on the LIVE VM (what the Accept OwlcatButton is wired to), with its click sound.
        // Confirm the outcome for the player case; a selected crusade army gets the game's own "set
        // destination" warning, so stay quiet there to avoid doubling it.
        private static void AcceptLive()
        {
            var vm = PanelVm();
            if (vm == null) return;
            PlayClick();
            var army = Game.Instance.GlobalMapController != null ? Game.Instance.GlobalMapController.SelectedArmy : null;
            bool entering = vm.IsCurrentLocation;
            var name = GlobalMapEnterPanel.Title(vm);
            vm.Accept();
            if (army == null) Tts.Speak(Loc.T(entering ? "worldmap.entering" : "worldmap.traveling", new { name }));
        }
    }
}
