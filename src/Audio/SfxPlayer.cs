using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Fire-and-forget one-shot sound player via NAudio (independent of the game's Wwise) — for discrete
    /// cues like fog enter/exit or an interactable ping. A persistent output device feeds a
    /// <see cref="MixingSampleProvider"/>; <see cref="Play"/> adds a voice the mixer auto-removes when it
    /// ends. Clips are decoded once and normalised to the mixer format (44.1 kHz stereo float — so any
    /// source rate/channel count works), cached by path. Distinct from <see cref="WallToneEngine"/>, which
    /// is the continuous looping wall tones.
    /// </summary>
    internal sealed class SfxPlayer : IDisposable
    {
        private const int Rate = 44100;

        private IWavePlayer _out;
        private MixingSampleProvider _mixer;
        private readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        public void Play(string path, float volume = 1f, float pan = 0f)
        {
            try
            {
                EnsureStarted();
                var buf = Get(path);
                if (buf != null && buf.Length > 0) _mixer.AddMixerInput(new OneShot(buf, Rate, volume, pan));
            }
            catch (Exception e) { Main.Log?.Error("[sfx] play failed: " + path + " — " + e); }
        }

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            _out = new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        private float[] Get(string path)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var buf = Decode(path);
            _cache[path] = buf;
            return buf;
        }

        // Decode a WAV and normalise to the mixer format (44.1 kHz stereo float, interleaved).
        private static float[] Decode(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                ISampleProvider sp = reader;
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);

                var all = new List<float>(Rate); // grows as needed
                var tmp = new float[Rate * 2];    // ~1 s of stereo
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null;
            _mixer = null;
        }

        // Plays a cached stereo-float buffer once; returns < count at the end so MixingSampleProvider
        // drops it automatically.
        private sealed class OneShot : ISampleProvider
        {
            private readonly float[] _buf;
            private readonly float _gainL, _gainR;
            private int _pos;

            public OneShot(float[] buf, int rate, float vol, float pan)
            {
                _buf = buf;
                // Constant-power pan: -1 full left, +1 full right, 0 centred (0.707 each). The buffer is
                // interleaved stereo (index parity = channel), so apply the per-channel gain by parity.
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _gainL = vol * (float)Math.Cos(t);
                _gainR = vol * (float)Math.Sin(t);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int remaining = _buf.Length - _pos;
                int n = Math.Min(count, remaining);
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = _buf[_pos + i] * (((_pos + i) & 1) == 0 ? _gainL : _gainR);
                _pos += n;
                return n;
            }
        }
    }
}
