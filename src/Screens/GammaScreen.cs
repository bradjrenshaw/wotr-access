using System.Collections.Generic;
using System.Linq;
using Kingmaker; // GameStarter
using Kingmaker.UI.MVVM._PCView.Settings.GammaCorrection; // GammaCorrectionPCView
using Kingmaker.UI.MVVM._VM.Settings.Entities; // SettingsEntitySliderVM
using Kingmaker.UI.MVVM._VM.Settings.GammaCorrection;     // GammaCorrectionVM
using Owlcat.Runtime.UI.MVVM; // IHasViewModel
using UnityEngine; // Resources
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The first-launch brightness/gamma + contrast calibration (<see cref="GammaCorrectionVM"/>), which
    /// GameStarter shows BEFORE the main menu and which BLOCKS boot (its coroutine spins on
    /// <c>gammaCorrectionIsChosen</c>) until the player confirms. It's a visual setup, so most players just
    /// confirm — but low-vision players can adjust it: we expose the same gamma + contrast sliders the game's
    /// own view builds (<c>new SettingsEntitySliderVM(vm.GammaCorrection/Contrast)</c>), which write the
    /// setting's temp value (applied live), then "Continue" confirms+saves it (VM.Close — which also marks
    /// gamma touched so this never auto-shows again, and fires the close callback that releases GameStarter's
    /// wait loop). "Reset to default" restores the default gamma/contrast.
    ///
    /// Detected via <c>GameStarter.IsGammaCorrectionActive</c> (a public static the boot block toggles); the
    /// live VM comes from the bound <see cref="GammaCorrectionPCView"/> (mouse mode) via IHasViewModel. Our
    /// mod is already loaded at this point — mods start at GameStarter line 259, the gamma block is at 471 —
    /// and focus mode is on, so our normal screen/nav plumbing works.
    ///
    /// To re-trigger first-time setup for testing: in <c>general_settings.json</c> (LocalLow, game closed)
    /// set <c>"settings.graphics.settings.graphics.gamma-correction-new-was-touched"</c> to <c>false</c>.
    /// </summary>
    public sealed class GammaScreen : Screen
    {
        public GammaScreen() { Wrap = true; } // Tab cycles help ↔ sliders ↔ Continue ↔ Reset

        public override string Key => "ctx.gamma";
        public override string ScreenName => Loc.T("screen.gamma");
        public override int Layer => 40; // boot-time modal, above everything else
        public override bool Exclusive => true;

        public override bool IsActive() => GameStarter.IsGammaCorrectionActive && Vm() != null;

        private static GammaCorrectionVM Vm()
        {
            var view = Resources.FindObjectsOfTypeAll<GammaCorrectionPCView>().FirstOrDefault();
            return view is IHasViewModel h ? h.GetViewModel() as GammaCorrectionVM : null;
        }

        private GammaCorrectionVM _built;
        // The slider VMs we build over the game's UISettings entities — ours to dispose (the game disposes
        // its own copies; these are independent observers of the same underlying gamma/contrast settings).
        private readonly List<SettingsEntitySliderVM> _sliderVms = new List<SettingsEntitySliderVM>();

        public override void OnPush() { _built = null; Rebuild(); }
        public override void OnPop() { Clear(); DisposeSliders(); _built = null; }

        public override void OnUpdate()
        {
            // The VM is bound by GameStarter; if it appears after we activate, build then.
            var vm = Vm();
            if (vm != null && vm != _built)
            {
                Rebuild();
                Navigation.Attach(this);
            }
        }

        // No cancel on this screen (the game only offers Apply / Default) — map Back/Escape to proceed.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "gamma.continue"),
                _ => Vm()?.Close());
        }

        private void Rebuild()
        {
            Clear();
            DisposeSliders();
            var vm = Vm();
            _built = vm;
            if (vm == null) return;

            Add(new TextElement(() => Loc.T("gamma.help")));
            // The two sliders are one Tab-stop (a list — arrows move between them), matching Settings.
            var sliders = new ListContainer(Loc.T("gamma.sliders"));
            sliders.Add(MakeSlider(new SettingsEntitySliderVM(vm.GammaCorrection)));
            sliders.Add(MakeSlider(new SettingsEntitySliderVM(vm.Contrast)));
            Add(sliders);
            Add(new ProxyActionButton(() => Loc.T("gamma.continue"), () => true, () => vm.Close()));
            Add(new ProxyActionButton(() => Loc.T("gamma.reset"), () => true, () => vm.Reset()));
        }

        private ProxySlider MakeSlider(SettingsEntitySliderVM sv)
        {
            _sliderVms.Add(sv);
            return new ProxySlider(sv);
        }

        private void DisposeSliders()
        {
            foreach (var sv in _sliderVms) sv.Dispose();
            _sliderVms.Clear();
        }
    }
}
