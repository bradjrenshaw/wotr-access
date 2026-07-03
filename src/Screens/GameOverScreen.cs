using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.GameModes; // GameModeType
using Kingmaker.UI.MVVM._PCView.GameOver; // GameOverPCView
using Kingmaker.UI.MVVM._VM.GameOver; // GameOverVM
using Owlcat.Runtime.UI.MVVM; // IHasViewModel
using UnityEngine; // Resources
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game-over / death screen (<see cref="GameOverVM"/>), shown when the party wipes (or an essential
    /// unit dies, the kingdom falls, a quest fails). The game holds it per-context (InGameStaticPartVM for an
    /// in-area wipe, plus GlobalMapVM / KingdomVM), each binding a <see cref="GameOverPCView"/> — so we find
    /// whichever is bound via IHasViewModel, and one screen covers every context. Without this a blind player
    /// who wiped was stuck. It's TERMINAL: no Back (Escape just re-reads it); the only ways out are its own
    /// actions — Quick load (latest save), Load game (opens the accessible save/load window), Main menu.
    ///
    /// Layer 18: above the in-game HUD, but BELOW the save/load window (20) so "Load game" opens navigably on
    /// top of it. Detected via CurrentMode == GameOver (cheap) before the bound-view lookup.
    /// </summary>
    public sealed class GameOverScreen : Screen
    {
        public GameOverScreen() { Wrap = true; } // Tab cycles reason ↔ buttons

        public override string Key => "ctx.gameover";
        public override string ScreenName => Loc.T("screen.game_over");
        public override int Layer => 18;
        public override bool Exclusive => true;

        public override bool IsActive()
            => Game.Instance != null && Game.Instance.CurrentMode == GameModeType.GameOver && Vm() != null;

        private static GameOverVM Vm()
        {
            foreach (var view in Resources.FindObjectsOfTypeAll<GameOverPCView>())
                if (view is IHasViewModel h && h.GetViewModel() is GameOverVM vm) return vm;
            return null;
        }

        private GameOverVM _built;

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

        // Terminal — there's nothing to go back to; Escape just re-reads the screen rather than popping it.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "screen.game_over"),
                _ => Tts.Speak(ScreenName, interrupt: true));
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _built = vm;
            if (vm == null) return;

            // The defeat reason (game-localized, e.g. "Your party has been defeated") — focusable so it reads.
            if (!string.IsNullOrEmpty(vm.Reason.Value)) Add(new TextElement(() => vm.Reason.Value));
            if (vm.CanQuickLoad.Value)
                Add(new ProxyActionButton(() => Loc.T("gameover.quick_load"), () => true, () => vm.OnQuickLoad()));
            Add(new ProxyActionButton(() => Loc.T("gameover.load"), () => true, () => vm.OnButtonLoadGame()));
            Add(new ProxyActionButton(() => Loc.T("gameover.main_menu"), () => true, () => vm.OnButtonMainMenu()));
        }
    }
}
