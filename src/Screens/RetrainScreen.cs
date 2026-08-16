using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.Retrain;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The RETRAIN character picker (<c>RetrainContextVM.RetrainVM</c> on the in-game static HUD) —
    /// the popup a respec NPC's dialogue option opens ("which character do you want to retrain?").
    /// It was a silent visual modal: the mod user heard only the dialogue behind it (tester repro,
    /// Defender's Heart / camp respec NPCs). One list of eligible characters (name + level);
    /// Enter = <c>OnConfirm(unit)</c> — the game then asks its own Yes/No confirm, which
    /// <see cref="MessageModalScreen"/> reads, and a Yes drops into the respec level-up wizard the
    /// mod already covers. Escape/Close = <c>OnClose()</c>. Layer 17 — above the dialogue (15)
    /// that spawns it.
    /// </summary>
    public sealed class RetrainScreen : Screen
    {
        public override string Key => "overlay.retrain";
        public override string ScreenName => Loc.T("screen.retrain");
        public override int Layer => 17;
        public override bool IsActive() => Vm() != null;

        private static RetrainVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.RetrainContextVM?.RetrainVM?.Value;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.OnClose());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "retrain:" + vm.GetHashCode() + ":"; // a new VM = a fresh window = fresh keys

            b.BeginStop(k + "chars").PushContext(Loc.T("screen.retrain"), "list");
            foreach (var unit in vm.SelectCharacters)
            {
                var u = unit;
                if (u == null) continue;
                b.AddItem(ControlId.Structural(k + u.UniqueId),
                    GraphNodes.Button(() => CharLabel(u), () => vm.OnConfirm(u)));
            }
            b.PopContext();

            b.BeginStop(k + "close").AddItem(ControlId.Structural(k + "close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => vm.OnClose()));
        }

        // Name + level — what the sighted portrait grid conveys at a glance.
        private static string CharLabel(Kingmaker.EntitySystem.Entities.UnitEntityData u)
            => Loc.T("retrain.char", new { name = u.CharacterName, level = u.Descriptor.Progression.CharacterLevel });
    }
}
