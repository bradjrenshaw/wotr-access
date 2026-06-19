using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using UnityEngine;
using WrathAccess.Exploration; // GlobalMapModel, Geo
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map (global map) base context — replaces the bare placeholder screen. INCREMENT 1:
    /// recognise the map and browse the revealed locations (distance-sorted, with bearing + state); Enter
    /// travels to a location (or enters the area you're standing on), driving the game's own
    /// <c>GoToLocationRevealed</c>/<c>EnterLocation</c>. The spatial cursor/sonar/scanner reuse, the
    /// world-map hotkeys (<c>/ . , m b</c>), armies, the full multi-option enter panel, and the
    /// Exploration/Scanner setting additions come in later increments.
    /// </summary>
    public sealed class GlobalMapScreen : Screen
    {
        public override string Key => "ctx.globalmap";
        public override string ScreenName => Loc.T("screen.world_map");
        public override int Layer => 0; // base context, like ctx.ingame

        public override bool IsActive() => GlobalMapModel.Active;

        private bool _built;
        public override void OnPush() { _built = false; }
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
                list.Add(new ProxyActionButton(() => LocationLabel(pv), () => true, () => Activate(pv)));
            }
            if (list.Children.Count > 0) Add(list);
            Navigation.Attach(this);
        }

        // Name + compass bearing from the party + a state tag (here / closed).
        private static string LocationLabel(GlobalMapPointView p)
        {
            var parts = new List<string> { (string)p.Blueprint.Name };
            if (p.Blueprint == GlobalMapModel.CurrentLocation)
            {
                parts.Add(Loc.T("worldmap.you_are_here"));
            }
            else
            {
                var bearing = Geo.Bearing(GlobalMapModel.TravelerPos, p.transform.position);
                if (!string.IsNullOrEmpty(bearing)) parts.Add(bearing);
                if (p.State.IsClosed) parts.Add(Loc.T("worldmap.closed"));
            }
            return string.Join(", ", parts);
        }

        // Enter on a location: enter the area if we're standing on it, else travel there. Mirrors the two
        // common branches of GlobalMapEnterMessageVM.Accept (the resource/warcamp/settlement branches and the
        // full multi-option panel come later); announces the outcome / the reason it can't proceed.
        private void Activate(GlobalMapPointView pv)
        {
            var view = GlobalMapView.Instance;
            if (view == null) return;
            string name = (string)pv.Blueprint.Name;

            if (pv.Blueprint == GlobalMapModel.CurrentLocation)
            {
                if (pv.State.IsClosed) { Tts.Speak(Loc.T("worldmap.is_closed", new { name })); return; }
                view.EnterLocation();
                Tts.Speak(Loc.T("worldmap.entering", new { name }));
                return;
            }

            var path = view.CalculatePlayerPathToLocation(pv.Blueprint);
            if (path == null) { Tts.Speak(Loc.T("worldmap.no_route", new { name })); return; }
            view.GoToLocationRevealed(pv);
            Tts.Speak(Loc.T("worldmap.traveling", new { name }));
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
