using System.Collections.Generic;
using System.IO;
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using WrathAccess.Audio;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The world-map point readout — a WorldMap-scoped system over the world-map cursor
    /// (<see cref="Cursor.WorldMap"/>), the map's analogue of the in-area object cue: a one-shot cue when
    /// the cursor crosses onto or off a thing (a location / junction, or a revealed army; each by its real
    /// clickable footprint), and the readout parts for the one pipeline — on a <b>Settle</b> what you
    /// stopped on, once, quiet over nothing; on a tile <b>Step</b> the things, else the bearing and miles
    /// from the party (so each step still places the cursor for tracking); on <b>Demand</b> the things,
    /// else "nothing here". An army camped on a point reads as both, army first. Reuses the shared
    /// object-cue volume (Audio tab). Pauses while a location panel is open, like the cursor.
    /// </summary>
    internal sealed class GlobalMapPointSystem : OverlaySystem
    {
        public override string Name => "World map point readout";
        public override string Key => "worldmap_point";
        public override OverlayScope Scope => OverlayScope.WorldMap;

        // A readout + a crossing cue: Off/Continuous only ("when moving" would silence the settle half).
        public override IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        protected override bool MovingNow(Overlay overlay) => Cursor.WorldMap.MovingRecently;

        private GlobalMapPointView _inside;      // the point the cursor is on (for the enter/leave cue)
        private GlobalMapArmyState _insideArmy;  // ... and the army
        private GlobalMapPointView _spoken;      // last point the settle readout spoke (null = armed)
        private GlobalMapArmyState _spokenArmy;
        private bool _baselined;                 // don't fire the cue on the first tick / on entering the map

        private void Baseline() { _inside = null; _insideArmy = null; _spoken = null; _spokenArmy = null; _baselined = false; }
        public override void OnExit(Overlay overlay) => Baseline();

        public override void Tick(float dt, Overlay overlay)
        {
            var cursor = Cursor.WorldMap;
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !cursor.Space.CanMove) { Baseline(); return; }

            var inside = GlobalMapCursor.NearestWithin();
            var army = GlobalMapCursor.ArmyWithin();
            // Object cue: same wavs + shared volume as the in-area ObjectCueSystem. Fires on a change of
            // what we're inside — enter when arriving on something (incl. a discrete tiled jump), leave to none.
            if (!_baselined) { _inside = inside; _insideArmy = army; _spoken = inside; _spokenArmy = army; _baselined = true; return; }
            if (inside != _inside || army != _insideArmy)
            {
                bool onSomething = inside != null || army != null;
                float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
                AudioEngines.Current.Play2D(Path.Combine(OverlayAudio.Dir, onSomething ? "object_enter.wav" : "object_exit.wav"), vol);
                _inside = inside; _insideArmy = army;
            }
            // Leaving a thing re-arms the settle readout for the next one (see Announce).
            if (inside == null) _spoken = null;
            if (army == null) _spokenArmy = null;
        }

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled) yield break;
            // Live (a step moved the cursor this same frame). Army first: its pawn sits on top of the point.
            var inside = GlobalMapCursor.NearestWithin();
            var army = GlobalMapCursor.ArmyWithin();
            bool sayArmy = army != null, sayPoint = inside != null;
            if (ctx.Trigger == ReadoutTrigger.Settle)
            {
                // Only what is new since the last stop (an idle cursor never repeats itself).
                sayArmy &= army != _spokenArmy;
                sayPoint &= inside != _spoken;
            }
            _spoken = inside; _spokenArmy = army;
            if (sayArmy) yield return new OverlayAnnouncement(ctx.Want, Message.Raw(GlobalMapActions.ArmyInPlace(army)), OverlayAnnouncement.OrderContents);
            if (sayPoint) yield return new OverlayAnnouncement(ctx.Want, Message.Raw(GlobalMapActions.InPlace(inside)), OverlayAnnouncement.OrderContents + 1);
            if (sayArmy || sayPoint || ctx.Trigger == ReadoutTrigger.Settle) yield break;
            // Nothing under the cursor: a step still places it (bearing + miles); a demand says so.
            yield return ctx.Trigger == ReadoutTrigger.Step
                ? new OverlayAnnouncement(ctx.Want, Message.Raw(GlobalMapActions.PositionAt(ctx.Cursor)), OverlayAnnouncement.OrderPosition)
                : new OverlayAnnouncement(ctx.Want, Message.Localized("ui", "worldmap.cursor_empty"), OverlayAnnouncement.OrderContents);
        }
    }
}
