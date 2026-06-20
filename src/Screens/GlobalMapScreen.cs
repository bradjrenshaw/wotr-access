using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using UnityEngine;
using WrathAccess.Exploration; // GlobalMapModel, GlobalMapActions, GlobalMapScanner, Geo
using WrathAccess.Input; // InputCategory
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map (global map) base context — replaces the bare placeholder screen. Two ways to browse:
    /// a Tab-stop location list (arrow through it, Enter travels/enters), and the isolated
    /// <see cref="GlobalMapScanner"/> (PageUp/Down points, Ctrl+PageUp/Down categories, I travel/enter) on
    /// the dedicated <see cref="InputCategory.WorldMap"/> keys. Travel/enter drives the game's own
    /// <c>GoToLocationRevealed</c>/<c>EnterLocation</c> via <see cref="GlobalMapActions"/>. The spatial
    /// cursor/sonar, the <c>/ . , m b</c> review hotkeys, armies, the full enter panel, and the
    /// Exploration/Scanner setting additions come in later increments.
    /// </summary>
    public sealed class GlobalMapScreen : Screen
    {
        public override string Key => "ctx.globalmap";
        public override string ScreenName => Loc.T("screen.world_map");
        public override int Layer => 0; // base context, like ctx.ingame

        public override bool IsActive() => GlobalMapModel.Active;

        // Starts unfocused: arrows/WASD drive the movement cursor and Tab enters the location list — exactly
        // like the in-game screen. Category order flips with focus so the arrows/Enter go to the cursor when
        // unfocused and to the list when focused; the scanner/review/cursor letter+page keys are WorldMap-only
        // either way (no clash with the list's UI arrows/Tab/Enter).
        public override bool StartUnfocused => true;
        private static readonly IReadOnlyList<InputCategory> Focused = new[] { InputCategory.UI, InputCategory.WorldMap };
        private static readonly IReadOnlyList<InputCategory> Unfocused = new[] { InputCategory.WorldMap, InputCategory.UI };
        public override IReadOnlyList<InputCategory> InputCategories => Navigation.HasFocus ? Focused : Unfocused;

        // Letters are world-map hotkeys (b/m/n review, i interact), not type-ahead — same as the in-game screen.
        public override bool AllowsTypeahead => false;

        private bool _built;
        public override void OnPush() { _built = false; GlobalMapScanner.Reset(); GlobalMapCursor.Reset(); GlobalMapSonar.Reset(); }
        public override void OnPop() { Clear(); _built = false; }

        public override void OnUpdate()
        {
            // Build once on entry. Reveal/travel changes refresh on re-entry for now; live updates land with
            // the cursor/scanner increment.
            if (!_built && GlobalMapModel.Active) { _built = true; Rebuild(); }
        }

        private void Rebuild()
        {
            Clear();
            var from = GlobalMapModel.TravelerPos;
            var list = new ListContainer(Loc.T("worldmap.locations"));
            foreach (var p in GlobalMapModel.Locations.OrderBy(p => Geo.Distance(from, p.transform.position)))
            {
                var pv = p; // capture per-iteration for the closures
                list.Add(new ProxyActionButton(() => GlobalMapActions.Label(pv), () => true, () => GlobalMapActions.Go(pv)));
            }
            if (list.Children.Count > 0) Add(list);
            Navigation.Attach(this);
        }

        // Escape opens the game menu (the game's own EscManager is muted while focus mode owns the keyboard),
        // matching the in-game screen.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }
    }
}
