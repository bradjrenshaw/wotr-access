using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>The stereo placement cues for one source, heard from the cursor (our virtual listener).</summary>
    internal struct SpatialCue
    {
        public float Pan;        // -1 (hard left/west) .. +1 (hard right/east), constant-power
        public float ItdSamples; // interaural delay; magnitude = samples, sign = +east / -west (far ear delayed)
        public float LowpassHz;  // the wet (rear) path's BiQuad cutoff; lower = more muffled
        public float WetMix;     // 0 = fully dry (ahead/side) .. up to MaxWet behind; how much of the filtered
                                 // signal replaces the dry one. The dry remainder keeps bright, narrowband cues
                                 // (which a lowpass would erase) audible — behind then reads as quieter/darker.
    }

    /// <summary>A playing positional voice whose placement can be re-set live (from the main thread) as the
    /// listener (cursor) moves — so a one-shot still tracks pan/gain/ITD/filter while it's audible, instead
    /// of freezing at fire time. Updates are smoothed inside the voice so a moving source never clicks.</summary>
    internal interface ISpatialVoice
    {
        bool Finished { get; }                       // drained — safe to drop from tracking
        void SetPlacement(SpatialCue cue, float volume);
    }

    /// <summary>
    /// Turns a source's listener-relative position into stereo cues, in the top-down XZ plane where
    /// +x is east (right) and +z is north (ahead/away). Three independent perceptual channels:
    ///
    ///  - <b>east/west → pan + ITD.</b> Constant-power amplitude pan (the level difference) plus an
    ///    <i>interaural time difference</i>: the far ear hears the sound a few samples later. Below ~1.5 kHz
    ///    ITD is the dominant localisation cue and the brain resolves it far finer than a single sample, so
    ///    it sharpens and "externalises" left/right far better than panning alone — especially on headphones.
    ///  - <b>distance → gain.</b> Left to the caller (each sound has its own falloff curve); this only does
    ///    direction. Gain is a magnitude, so it can't tell front from back — that's the next channel's job.
    ///  - <b>north/south → timbre.</b> Stereo can't pan front/back, so sources <i>behind</i> the listener
    ///    (south of it) are progressively low-passed — muffled = behind, bright = ahead. This is the
    ///    long-standing audiogame convention for resolving the front/back ambiguity that pan leaves.
    ///
    /// Both extra cues are individually toggleable (the Audio tab) so they can be A/B'd by ear.
    /// </summary>
    internal static class Spatializer
    {
        public const int Rate = NAudioEngine.Rate;

        // Max interaural delay ≈ head width / speed of sound ≈ 0.22 m / 343 m/s ≈ 0.66 ms.
        private const float MaxItdSeconds = 0.00066f;
        private static float MaxItdSamples => MaxItdSeconds * Rate; // ~29 @ 44.1 kHz

        // Front/back cue. The wet path is a lowpass closing from open (due-side) to muffled (due-south); the
        // wet MIX rises in step. Because our cues are bright and narrowband (review.wav has ~no energy below
        // 1 kHz), a pure lowpass would silence them behind you — so the dry remainder (1 − WetMix) is always
        // kept: broadband sounds darken, bright sounds simply get quieter, and nothing ever disappears.
        private const float OpenHz = 20000f;    // due-side: wet path effectively transparent (and WetMix ≈ 0)
        private const float MuffledHz = 500f;   // due-south: the wet path is heavily muffled
        private const float MaxWet = 0.5f;      // due-south: 50% filtered / 50% dry (a bright cue → ~−6 dB)

        public static bool ItdEnabled => ModSettings.GetSetting<BoolSetting>("audio.itd")?.Get() ?? true;
        public static bool FilterEnabled => ModSettings.GetSetting<BoolSetting>("audio.front_back_filter")?.Get() ?? true;

        /// <summary>Direction cues for a source offset from the listener (metres). <paramref name="panWidth"/>
        /// is the lateral crossover — within it, pan tracks absolute sideways offset; beyond it, pure bearing.</summary>
        public static SpatialCue Cue(float dxEast, float dzNorth, float panWidth)
        {
            float dist = Mathf.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            float lat = dist > 1e-4f ? Mathf.Clamp(dxEast / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;

            var cue = new SpatialCue { Pan = lat, LowpassHz = OpenHz, WetMix = 0f };

            // ITD shares the pan's lateral fraction so the time and level cues move together.
            if (ItdEnabled) cue.ItdSamples = MaxItdSamples * lat;

            // Front/back: only the rear hemisphere is processed (matches "south of the listener" exactly),
            // ramping from dry at the due-side line to muffled-and-mixed-in at due-south.
            if (FilterEnabled && dist > 1e-4f)
            {
                float northFrac = Mathf.Clamp(dzNorth / dist, -1f, 1f); // +1 ahead .. -1 behind
                if (northFrac < 0f)
                {
                    float back = -northFrac; // 0 at the side line .. 1 at due-south
                    cue.LowpassHz = OpenHz * Mathf.Pow(MuffledHz / OpenHz, back); // log interp, open → muffled
                    cue.WetMix = back * MaxWet;
                }
            }
            return cue;
        }
    }
}
