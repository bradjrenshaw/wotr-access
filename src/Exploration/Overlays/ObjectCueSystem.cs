using System.IO;
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A one-shot cue when the cursor enters or leaves an object's footprint (units and interactables;
    /// nearest wins). Enter fires on a change to a real object (including swapping straight from one to
    /// another); exit fires only when leaving to none. Self-gates on <see cref="OverlayManager.Active"/>.
    /// </summary>
    internal sealed class ObjectCueSystem : AudioSystem
    {
        public override string Name => "Object cue";
        public override string Key => "object";

        private readonly SfxPlayer _sfx = new SfxPlayer();
        private ScanItem _inside;   // the object the cursor is currently inside (nearest), or null
        private bool _baselined;    // false until the first active tick (don't fire on entry)

        private const float LevelGap = 3f; // ignore objects on another level for "inside" tests

        public override void OnExit(Overlay overlay) { _inside = null; _baselined = false; }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !Enabled) { _inside = null; _baselined = false; return; }

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
                if ((it.SonarSound != null || it.IsUnit) && dist <= fp && dist < best && Mathf.Abs(p.y - c.y) <= LevelGap)
                {
                    best = dist; inside = it;
                }
            }

            if (!_baselined) { _inside = inside; _baselined = true; }
            else if (inside != _inside)
            {
                _sfx.Play(Path.Combine(OverlayAudio.Dir, inside != null ? "object_enter.wav" : "object_exit.wav"), EffectiveVolume);
                _inside = inside;
            }
        }
    }
}
