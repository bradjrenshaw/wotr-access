using System.IO;
using Kingmaker.Controllers; // FogOfWarController
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A one-shot cue when the cursor crosses the fog-of-war boundary (enter / exit). Self-gates on
    /// <see cref="OverlayManager.Active"/>; the baseline resets when inactive so re-activating doesn't fire
    /// a spurious cue. (The spoken "fog of war" status lives in the tile readout for now.)
    /// </summary>
    internal sealed class FogSystem : OverlaySystem
    {
        public override string Name => "Fog cue";

        private readonly SfxPlayer _sfx = new SfxPlayer();
        private bool? _wasFogged; // null = no baseline yet (don't fire on the first sample)

        public override void OnExit(Overlay overlay) => _wasFogged = null;

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) { _wasFogged = null; return; }

            var c = overlay.Cursor.Position;
            bool fogged = FogOfWarController.IsInFogOfWar(c);
            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
                _sfx.Play(Path.Combine(OverlayAudio.Dir, fogged ? "fog_enter.wav" : "fog_exit.wav"));
            _wasFogged = fogged;
        }
    }
}
