using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings.ActionTexts (Drop/Split/Move — game-localized)
using Kingmaker.UI.MVVM._VM.CounterWindow;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's COUNTER window (CommonVM.CounterWindowVM) — the stack-amount picker behind
    /// inventory Split, partial Drop/Move, and vendor stack quantities. It was a silent visual
    /// modal before this screen ("split does nothing" tester repro). Mirrors CounterWindowPCView's
    /// contract exactly: the amount is 1..MaxValue written straight to <c>vm.CurrentValue</c> (the
    /// sighted slider does the same), <c>Accept()</c> commits, <c>Close()</c> cancels (Escape / the
    /// view's EscManager). The action button carries the game's own localized Drop/Split/Move word.
    /// Layer 31 — above the service windows and the loot/vendor family that spawn it.
    /// </summary>
    public sealed class CounterWindowScreen : Screen
    {
        public CounterWindowScreen() { Wrap = true; } // Tab cycles amount ↔ buttons

        public override string Key => "overlay.counter";
        // The screen announces itself as "<action>: <item>" — read once on entry, with focus landing
        // straight on the amount slider (picking a number is the window's whole job).
        public override string ScreenName
        {
            get
            {
                var vm = Vm();
                return vm != null ? ActionWord(vm.OperationType) + ": " + vm.ItemName : Loc.T("screen.counter");
            }
        }
        public override int Layer => 31;
        public override bool IsActive() => Vm() != null;

        private static CounterWindowVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.CounterWindowVM.Value
                : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "counter:" + vm.GetHashCode() + ":";

            // The amount slider (the landing element): Left/Right ±1, Ctrl (large) ±10, clamped to
            // the sighted slider's 1..MaxValue; the value is spoken as feedback after each step.
            b.BeginStop("amount").AddItem(ControlId.Structural(k + "amount"), new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("counter.amount")),
                    new NodeAnnouncement(() => AmountText(vm), kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("counter.amount"),
                StateText = () => AmountText(vm),
                OnAdjust = (sign, large) =>
                {
                    int before = vm.CurrentValue;
                    vm.CurrentValue = UnityEngine.Mathf.Clamp(before + sign * (large ? 10 : 1), 1, vm.MaxValue);
                    if (vm.CurrentValue != before)
                        UiSound.Play(Kingmaker.UI.UISoundType.SettingsSliderMove);
                },
            });

            b.BeginStop("accept").AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(() => ActionWord(vm.OperationType), () => vm.Accept()));
            b.BeginStop("cancel").AddItem(ControlId.Structural(k + "cancel"),
                GraphNodes.Button(() => Loc.T("action.cancel"), () => vm.Close()));
        }

        // The game's own localized action word (Drop item / Split stack / Move) — passed through.
        private static string ActionWord(CounterWindowType t)
        {
            var a = UIStrings.Instance?.ActionTexts;
            if (a == null) return "";
            switch (t)
            {
                case CounterWindowType.Drop: return a.DropItem;
                case CounterWindowType.Split: return a.SplitItem;
                default: return a.MoveItem; // Move + MoveMinValueOnEnter share the label (as the view does)
            }
        }

        // The spoken value: split reads "take X, leaving Y" (the two halves the sighted count text
        // shows); drop/move read "X of max".
        private static string AmountText(CounterWindowVM vm)
            => vm.OperationType == CounterWindowType.Split
                ? Loc.T("counter.amount_split", new { value = vm.CurrentValue, rest = vm.MaxValue - vm.CurrentValue + 1 })
                : Loc.T("counter.amount_of", new { value = vm.CurrentValue, max = vm.MaxValue });
    }
}
