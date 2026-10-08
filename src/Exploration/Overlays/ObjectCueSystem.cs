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

        // Enter/exit cue is move-driven but the idle-hover announce fires when STOPPED, so "when moving"
        // would suppress half its job — Off/Continuous only.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

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
            // Control-gated like every other audio system: during cutscenes/dialogue the cursor is
            // parked while scripted units stream under it — without the gate the intro cutscene fires
            // hundreds of enter/exit blips. Resetting the baseline also swallows the state churn, so
            // control's return doesn't replay it.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl)
                { _inside = null; _spoken = null; _baselined = false; return; }

            var c = Cursor.Area.Position;
            ScanItem inside = null;
            float best = float.MaxValue;
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                if (ScanSounds.Resolve(it.Primary) == null && !it.IsUnit) continue;
                var p = it.Position;
                if (Mathf.Abs(p.y - c.y) > LevelGap) continue; // another level
                if (!it.Contains(c)) continue;                 // cursor inside the actual footprint shape
                float dx = p.x - c.x, dz = p.z - c.z, d = dx * dx + dz * dz; // nearest CENTRE wins ties
                if (d < best) { best = d; inside = it; }
            }

            if (!_baselined) { _inside = inside; _spoken = inside; _baselined = true; return; }
            if (inside != _inside)
            {
                AudioEngines.Current.Play2D(Path.Combine(OverlayAudio.Dir, inside != null ? "object_enter.wav" : "object_exit.wav"), EffectiveVolume);
                _inside = inside;
            }

            // Leaving the object re-arms the settle readout for the next one (see Announce).
            if (inside == null) _spoken = null;
        }

        /// <summary>The thing under the cursor, for the readout pipeline: on a Settle, only when it is
        /// new since the last settle (an idle cursor never repeats itself) and the hover option is on;
        /// on Demand, always. Not on a tile Step — the grid's tile readout lists the cell's contents.</summary>
        public override System.Collections.Generic.IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled || ctx.Want != AnnouncementContext.Point) yield break;
            var inside = _inside;
            if (inside == null) { _spoken = null; yield break; }
            if (ctx.Trigger == ReadoutTrigger.Settle)
            {
                if (!Bool("announce_hover", true) || inside == _spoken) yield break;
                _spoken = inside;
            }
            else if (ctx.Trigger == ReadoutTrigger.Step) yield break;
            yield return new OverlayAnnouncement(AnnouncementContext.Point, Message.Raw(inside.DescribeInPlace()), OverlayAnnouncement.OrderContents);
        }
    }
}
