using System.IO;
using Kingmaker.Controllers; // FogOfWarController
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A one-shot cue when the cursor crosses the fog-of-war boundary (enter / exit) — in an area AND on
    /// the world map (the map draws the same fog renderer around the party and your armies, so the game's
    /// one fog oracle answers for both; this system follows whichever cursor the live context has).
    /// Self-gates on <see cref="OverlayManager.Active"/>; the baseline resets when inactive or when the
    /// context changes so re-activating doesn't fire a spurious cue. (The spoken "fog of war" status lives
    /// in the tile readout for now.)
    /// </summary>
    internal sealed class FogSystem : AudioSystem
    {
        public override string Name => "Fog cue";
        public override string Key => "fog";
        public override OverlayScope Scope => OverlayScope.Both;

        // A crossing event — "when moving" can't differ from "continuous" (you only cross by moving).
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        private bool? _wasFogged; // null = no baseline yet (don't fire on the first sample)
        private OverlayScope _scope;  // the context the baseline was taken in

        public override void OnExit(Overlay overlay) => _wasFogged = null;

        public override void Tick(float dt, Overlay overlay)
        {
            // The live context's cursor; its own movement gate (control in an area — silent through a
            // cutscene, like the other audio systems; no open panel on the map).
            var scope = OverlayManager.CurrentScope;
            var cursor = scope == OverlayScope.WorldMap ? Cursor.WorldMap : Cursor.Area;
            if (scope != _scope) { _scope = scope; _wasFogged = null; }
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !cursor.Space.CanMove) { _wasFogged = null; return; }

            bool fogged = FogOfWarController.IsInFogOfWar(cursor.Position);
            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
                AudioEngines.Current.Play2D(Path.Combine(OverlayAudio.Dir, fogged ? "fog_enter.wav" : "fog_exit.wav"), EffectiveVolume);
            _wasFogged = fogged;
        }
    }
}
