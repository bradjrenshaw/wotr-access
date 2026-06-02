using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.Loot;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The loot window (<see cref="LootVM"/>) shown when you loot a container or corpse — driven, like the
    /// dialogue screen, off a reactive on the in-game static HUD (<c>LootContextVM.LootVM</c>), not a
    /// RootUIContext service window. Each loot source (a corpse/chest = <see cref="LootObjectVM"/>) becomes
    /// its own Tab-stop list of items; arrows move within a source, Tab moves between sources. Enter on an
    /// item takes it; a "Take all" stop empties everything and closes; Escape closes. The party-stash side
    /// (moving items the other way, chest deposits) is deferred for now.
    /// </summary>
    public sealed class LootScreen : Screen
    {
        public override string Key => "ctx.loot";
        public override string ScreenName => "Loot";
        public override int Layer => 15; // over the in-game context + service windows, alongside dialogue

        private LootVM _builtVm; // the window the current tree was built for

        private static LootVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { _builtVm = null; }
        public override void OnPop() { Clear(); _builtVm = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // A new LootVM instance means a fresh window opened (each interaction builds a new one). Within
            // one window items only leave (collect), and the slot proxies drop out of nav themselves, so no
            // mid-window rebuild is needed.
            if (vm != _builtVm) { _builtVm = vm; Rebuild(vm); }
        }

        private void Rebuild(LootVM vm)
        {
            Clear();

            // One Tab-stop list per loot source.
            if (vm.ContextLoot != null)
            {
                foreach (var obj in vm.ContextLoot)
                {
                    if (obj?.SlotsGroup == null) continue;
                    var group = new ListContainer(string.IsNullOrEmpty(obj.DisplayName) ? "Loot" : obj.DisplayName);
                    foreach (var slot in obj.SlotsGroup.VisibleCollection)
                        if (slot != null && slot.HasItem) group.Add(new ProxyLootSlot(vm, slot));
                    if (group.Children.Count > 0) Add(group);
                }
            }

            // Take all — its own Tab-stop after the lists.
            Add(new ProxyActionButton("Take all", () => vm.HasItemsToLoot, vm.CollectAll));

            Navigation.Attach(this);
        }

        // Escape closes the window (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }
    }
}
