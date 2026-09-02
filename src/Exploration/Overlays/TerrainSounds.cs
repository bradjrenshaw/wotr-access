using Kingmaker;
using Kingmaker.Visual.Sound; // SurfaceTypeObject, FootSoundType
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// <b>Terrain sound under the cursor</b> (a cursor setting, not an overlay system): while the cursor
    /// moves, post the game's OWN humanoid footstep Wwise event (<c>FS_Humanoid</c> — the one every
    /// biped's walk clip fires via <c>UnitAnimationCallbackReceiver.PlayFootstep</c>) on a Wwise game
    /// object parked at the cursor, at a fixed cadence. The event's sound is picked by three switches
    /// exactly the way the game sets them before each unit step: <c>Terrain</c> from the area's baked
    /// surface map at the position (<see cref="SurfaceTypeObject.GetSurfaceSoundTypeSwitch"/>:
    /// ground/grass/stone/wood/water/…), and <c>FootType</c>/<c>FootSize</c> from the reference unit's
    /// blueprint (so it sounds like the party walking, not a generic boot). Loudness is the emitter's
    /// Wwise output-bus volume (the Footsteps volume slider times the master), with the game's
    /// <c>Audibility</c> RTPC pinned to fully visible (0.4 on that fog knob was near-silent by ear).
    /// Ticked by <see cref="Overlay.Tick"/> after the cursor moves; torn down with the overlay.
    /// </summary>
    internal static class TerrainSounds
    {
        public const string VolumeKey = "footsteps";
        private const string EventName = "FS_Humanoid";

        private static GameObject _emitter;     // the Wwise game object we post on (registered lazily)
        private static float _sinceStep = 999f; // so the first step fires as soon as movement starts
        private static uint _playing;           // the last posted step (stopped on key release)
        private static bool _keysHeld;
        private const int ReleaseFadeMs = 20;   // short enough to read as "stopped", long enough not to click

        private static CategorySetting CursorCat =>
            ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor");
        private static bool On => CursorCat?.Get<BoolSetting>("terrain_sounds")?.Get() ?? false;
        private static float IntervalSec => (CursorCat?.Get<IntSetting>("terrain_interval")?.Get() ?? 300) / 1000f;
        private static float Volume =>
            (ModSettings.GetSetting<IntSetting>("audio.volumes." + VolumeKey)?.Get() ?? 100) / 100f * OverlayAudio.Master;

        public static void RegisterSettings(CategorySetting cursorCat)
        {
            if (cursorCat.GetByKey("terrain_sounds") == null)
                cursorCat.Add(new BoolSetting("terrain_sounds", "Play terrain sound under cursor", false,
                    "overlay.cursor.terrain_sounds"));
            if (cursorCat.GetByKey("terrain_interval") == null)
                cursorCat.Add(new IntSetting("terrain_interval", "Terrain sound interval (milliseconds)", 300, 100, 1000, 50,
                    "overlay.cursor.terrain_interval"));
        }

        public static void Tick(float dt, Overlay overlay)
        {
            // Releasing the movement keys cuts the step in flight (user request): a step that starts
            // right as you let go otherwise trails on after the cursor has stopped.
            bool held = overlay.Cursor.MovementKeysHeld();
            if (_keysHeld && !held) StopCurrent();
            _keysHeld = held;
            if (!On || !OverlayManager.Active || !overlay.CursorMovingRecently || !WrathAccess.ControlState.HasControl)
            {
                _sinceStep = 999f; // next movement starts with an immediate step
                return;
            }
            _sinceStep += dt;
            if (_sinceStep < IntervalSec) return;
            _sinceStep = 0f;
            Step(overlay.Cursor.Position);
        }

        private static void Step(Vector3 pos)
        {
            var go = Emitter();
            if (go == null) return;
            // Wwise positions are set explicitly (no AkGameObj component driving it from a transform):
            // the emitter sits AT the cursor, which is also where ListenerAnchor puts the ears — centred.
            AkSoundEngine.SetObjectPosition(go, pos, Vector3.forward, Vector3.up);

            // The same three switches the game sets before a unit's footstep (PlayFootstep +
            // SetTerrainSwitch), from the same sources.
            var unit = CombatMode.ReferenceUnit ?? Game.Instance?.Player?.MainCharacter.Value;
            var vis = unit?.Blueprint?.Visual;
            var foot = vis?.FootSoundType ?? FootSoundType.Boot;
            if (foot == FootSoundType.None) foot = FootSoundType.Boot; // a footless blueprint still walks by ear
            AkSoundEngine.SetSwitch("FootType", foot.ToString(), go);
            AkSoundEngine.SetSwitch("FootSize", (vis?.FootSoundSize ?? Kingmaker.Enums.Size.Medium).ToString(), go);
            var surface = SurfaceTypeObject.GetSurfaceSoundTypeSwitch(pos);
            AkSoundEngine.SetSwitch("Terrain", (surface ?? Kingmaker.Sound.SurfaceSoundType.Ground).ToString(), go);

            // Volume: full audibility, then our slider as the emitter's output-bus volume (a null
            // listener = every listener).
            AkSoundEngine.SetRTPCValue("Audibility", 1f, go);
            AkSoundEngine.SetGameObjectOutputBusVolume(go, null, Mathf.Clamp01(Volume));
            AkSoundEngine.SetRTPCValue("CombatSpeed", 1f, go);
            _playing = AkSoundEngine.PostEvent(EventName, go);
        }

        private static void StopCurrent()
        {
            if (_playing == 0) return;
            try { if (AkSoundEngine.IsInitialized()) AkSoundEngine.StopPlayingID(_playing, ReleaseFadeMs); } catch { }
            _playing = 0;
            _sinceStep = 999f; // the next press starts with an immediate step
        }

        private static GameObject Emitter()
        {
            if (_emitter != null) return _emitter;
            if (!AkSoundEngine.IsInitialized()) return null;
            _emitter = new GameObject("WrathAccess.Footsteps");
            Object.DontDestroyOnLoad(_emitter);
            AkSoundEngine.RegisterGameObj(_emitter, _emitter.name);
            return _emitter;
        }

        /// <summary>Release the Wwise emitter (overlay exit / module teardown).</summary>
        public static void Teardown()
        {
            StopCurrent();
            _keysHeld = false;
            if (_emitter == null) return;
            try { if (AkSoundEngine.IsInitialized()) AkSoundEngine.UnregisterGameObj(_emitter); } catch { }
            Object.Destroy(_emitter);
            _emitter = null;
        }
    }
}
