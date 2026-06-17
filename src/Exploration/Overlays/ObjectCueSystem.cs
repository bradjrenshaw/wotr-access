using System.IO;
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A one-shot cue when the cursor enters or leaves an object's footprint (units and interactables;
    /// nearest wins). Enter fires on a change to a real object (including swapping straight from one to
    /// another); exit fires only when leaving to none. Self-gates on <see cref="OverlayManager.Active"/>.
    ///
    /// Also the <b>idle hover announce</b> (continuous mode): gliding is too fast to narrate, so nothing
    /// speaks on move — but once no movement key is held, whatever the cursor sits inside is spoken (on
    /// key release, or when something walks under the idle cursor). Tile mode already announces every
    /// step, so this only applies while the primary movement mode doesn't announce on move.
    /// </summary>
    internal sealed class ObjectCueSystem : AudioSystem
    {
        public override string Name => "Object cue";
        public override string Key => "object";

        private readonly SfxPlayer _sfx = new SfxPlayer();
        private ScanItem _inside;   // the object the cursor is currently inside (nearest), or null
        private ScanItem _spoken;   // what the idle hover announce last spoke (null = armed to announce)
        private bool _baselined;    // false until the first active tick (don't fire on entry)

        private const float LevelGap = 3f; // ignore objects on another level for "inside" tests

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.BoolSetting("announce_hover", "Announce hover when idle", true,
                "overlay.object.announce_hover"));
        }

        public override void OnExit(Overlay overlay) { _inside = null; _spoken = null; _baselined = false; }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !Enabled) { _inside = null; _spoken = null; _baselined = false; return; }

            var c = overlay.Cursor.Position;
            ScanItem inside = null;
            float best = float.MaxValue;
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                var p = it.Position;
                float dx = p.x - c.x, dz = p.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float fp = it.Footprint;
                if ((ScanSounds.Resolve(it.Primary) != null || it.IsUnit) && dist <= fp && dist < best && Mathf.Abs(p.y - c.y) <= LevelGap)
                {
                    best = dist; inside = it;
                }
            }

            if (!_baselined) { _inside = inside; _spoken = inside; _baselined = true; return; }
            if (inside != _inside)
            {
                _sfx.Play(Path.Combine(OverlayAudio.Dir, inside != null ? "object_enter.wav" : "object_exit.wav"), EffectiveVolume);
                _inside = inside;
            }

            // Idle hover announce. While the HUD owns the arrows, freeze the spoken state (a held arrow
            // there is UI nav, not movement — re-arming on it would cause spurious re-announces on release).
            if (WrathAccess.UI.Navigation.HasFocus) return;
            var mode = overlay.Cursor.ModeFor(MovementSlot.Primary);
            if (mode == null || mode.AnnouncesOnMove || !Bool("announce_hover", true)) { _spoken = null; return; }
            if (MoveKeyHeld() || inside == null) { _spoken = null; return; } // moving / over nothing → re-arm
            if (inside != _spoken)
            {
                Tts.Speak(inside.DescribeInPlace());
                _spoken = inside;
            }
        }

        private static bool MoveKeyHeld()
            => InputManager.Held("explore.cursorUp") || InputManager.Held("explore.cursorDown")
            || InputManager.Held("explore.cursorLeft") || InputManager.Held("explore.cursorRight")
            || InputManager.Held("explore.secondaryUp") || InputManager.Held("explore.secondaryDown")
            || InputManager.Held("explore.secondaryLeft") || InputManager.Held("explore.secondaryRight");
    }
}
