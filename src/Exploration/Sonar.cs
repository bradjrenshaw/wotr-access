using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Kingmaker;
using Kingmaker.Controllers; // FogOfWarController
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays; // OverlayManager
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Audio sonification of the surroundings, anchored at the overlay <see cref="Cursor"/> (the "listener").
    /// Two parts, ticked after the overlays each frame (so the cursor's new position is used):
    ///  • <b>Fog cue</b> — a one-shot when the cursor crosses the fog-of-war boundary.
    ///  • <b>Soundscape</b> — a continuous looping voice for every VISIBLE sonifiable thing
    ///    (<see cref="ScanItem.SonarSound"/>; interactables for now), volume by distance + pan by direction
    ///    from the cursor, so you build a live picture and home in by moving the cursor toward a sound.
    /// Membership is "visible", not range-capped (revealed things can be far), so volume must keep distant
    /// things audible (curve below — tunable). Active only while an overlay is up (Ctrl+O off → silence).
    /// Per-entity persistence comes from <see cref="WorldModel"/> (voices keyed by the item).
    /// </summary>
    internal static class Sonar
    {
        private static readonly SfxPlayer Sfx = new SfxPlayer();
        private static readonly Soundscape Scape = new Soundscape();
        private static readonly List<VoiceSpec> Specs = new List<VoiceSpec>();
        private static bool? _wasFogged;     // null = no baseline yet (don't fire on the first sample)
        private static ScanItem _insideItem; // the object the cursor is currently inside (nearest), or null
        private static bool _objBaselined;   // false until the first active tick (don't fire on entry)

        private const float LevelGap = 3f; // metres; ignore objects on another level for "inside" tests

        // Distance → volume: half at RefFeet, gently falling, floored so far-but-visible things stay
        // audible (no hard range cutoff — visibility is the cutoff). Tunable; we'll iterate on scaling.
        private const float RefFeet = 10f;
        private const float MinVol = 0.08f;
        // Pan crossover distance. Closer than this → pan by lateral offset (gentle, so close-but-off
        // things stay near centre); farther → pan by bearing (dx/dist) so distant things keep varying by
        // direction instead of all clamping to full pan past a fixed lateral width. (pan = dx/max(dist,W).)
        private const float PanWidthFeet = 10f;

        private static string AudioDir =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");

        public static void Tick()
        {
            if (!OverlayManager.Active || !Cursor.Has)
            {
                _wasFogged = null; _insideItem = null; _objBaselined = false; Scape.Clear();
                return;
            }
            var c = Cursor.Position.Value;

            // Fog of war: enter/exit cue on the cursor crossing the boundary.
            bool fogged = FogOfWarController.IsInFogOfWar(c);
            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
                Sfx.Play(Path.Combine(AudioDir, fogged ? "fog_enter.wav" : "fog_exit.wav"));
            _wasFogged = fogged;

            // Soundscape: a looping voice per visible sonifiable thing, placed relative to the cursor.
            float refDist = RefFeet * Geo.MetresPerFoot;
            float panWidth = PanWidthFeet * Geo.MetresPerFoot;
            ScanItem insideItem = null; // nearest object the cursor is inside this frame
            float insideBest = float.MaxValue;
            Specs.Clear();
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                var p = it.Position;
                float dx = p.x - c.x, dz = p.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float fp = it.Footprint;
                var snd = it.SonarSound;

                // Inside-an-object cue: units (corpulence) and interactables — NOT plain scenery or markers.
                // Nearest wins; markers (footprint 0) never count anyway.
                if ((snd != null || it.IsUnit) && dist <= fp && dist < insideBest && Mathf.Abs(p.y - c.y) <= LevelGap)
                {
                    insideBest = dist; insideItem = it;
                }

                // Soundscape voice: sonifiable things only (interactables for now).
                if (snd == null) continue;
                // Volume: within the footprint → max; outside → from the nearest surface (closest-point).
                // Pan: lateral offset up close, bearing farther out (dx/max(dist,W)); centred when within.
                float edge = Mathf.Max(0f, dist - fp);
                float vol = Mathf.Clamp(refDist / (refDist + edge), MinVol, 1f);
                float pan = dist > fp ? Mathf.Clamp(dx / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;
                Specs.Add(new VoiceSpec(it, Path.Combine(AudioDir, "interactables", snd + ".wav"), vol, pan));
            }
            Scape.Update(Specs);

            // Object enter/exit cue. Enter fires whenever the object under the cursor CHANGES to a real one
            // (including swapping straight from one object to another); exit only when leaving to none.
            if (!_objBaselined) { _insideItem = insideItem; _objBaselined = true; }
            else if (insideItem != _insideItem)
            {
                Sfx.Play(Path.Combine(AudioDir, insideItem != null ? "object_enter.wav" : "object_exit.wav"));
                _insideItem = insideItem;
            }
        }
    }
}
