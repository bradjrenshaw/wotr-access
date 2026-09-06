using System;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>Four directional wall-tone voices (order: ahead, behind, right, left — ear-fixed pans;
    /// the WallToneSystem rotates the TRACE directions by the listener facing), driven every frame with
    /// the trace hit point + a 0..1 proximity volume per direction. Dispose stops + releases the voices.
    /// (The Wwise backend and its engine-choice interface are gone — NAudio is the one audio engine.)</summary>
    internal interface IWallTones : IDisposable
    {
        void Update(Vector3[] hits, float[] volumes);
    }

    /// <summary>The mod's audio output backend — the consumer surface every cue/speech/wall-tone
    /// caller uses. Two implementations: <see cref="NAudioEngine"/> (managed mixer on a WaveOut
    /// buffer; compatible everywhere) and <see cref="LoadstarEngine"/> (the native low-latency
    /// mixer). Picked by the <c>audio.backend</c> setting via <see cref="AudioEngines.Current"/>.</summary>
    internal interface IAudioEngine : IDisposable
    {
        IWallTones CreateWallTones(string toneSet);
        void Play2D(string file, float volume);
        void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan);
        void PlayPcm(WrathAccess.Speech.SpeechAudio audio, float volume, float pan);
        ISpatialVoice PlaySpatial(string file, float volume, float dxEast, float dzNorth, float panWidth);
        /// <summary>Per frame from the module tick (device watchdogs, finished polls).</summary>
        void Tick();
    }
}
