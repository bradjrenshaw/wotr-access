using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WrathAccess.Audio
{
    /// <summary>One looping voice to play this frame: which thing (Key), its sound file, and where it
    /// sits relative to the listener (Volume by distance, Pan -1..1 left..right).</summary>
    internal struct VoiceSpec
    {
        public object Key;
        public string Path;
        public float Volume;
        public float Pan;
        public VoiceSpec(object key, string path, float volume, float pan)
        { Key = key; Path = path; Volume = volume; Pan = pan; }
    }

    /// <summary>
    /// Continuous spatial soundscape via NAudio (independent of Wwise): a looping voice per nearby thing,
    /// volume by distance and pan by direction relative to the cursor, all mixed to stereo. Voices are
    /// keyed by their <see cref="VoiceSpec.Key"/> (the WorldModel item) so they persist and track as the
    /// cursor moves. <see cref="Update"/> syncs the live voice set each frame (add new, update vol/pan,
    /// drop gone); <see cref="Clear"/> silences. Sound buffers are decoded to mono 44.1k once and shared
    /// across voices that use the same file. Mono so we can pan; distinct from the looping but fixed-channel
    /// <see cref="WallToneEngine"/> and the one-shot <see cref="SfxPlayer"/>.
    /// </summary>
    internal sealed class Soundscape : IDisposable
    {
        private const int Rate = 44100;

        private IWavePlayer _out;
        private Mixer _mixer;
        private readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        public void Update(List<VoiceSpec> specs)
        {
            try { EnsureStarted(); _mixer.Sync(specs, GetBuffer); }
            catch (Exception e) { Main.Log?.Error("[soundscape] update failed: " + e); }
        }

        public void Clear() => _mixer?.ClearVoices();

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new Mixer(Rate);
            _out = new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        private float[] GetBuffer(string path)
        {
            if (_cache.TryGetValue(path, out var b)) return b;
            b = DecodeMono(path);
            _cache[path] = b;
            return b;
        }

        // Decode a WAV to a mono float[] at the mixer rate (so panning works; any source rate/channels).
        private static float[] DecodeMono(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                ISampleProvider sp = reader;
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                int ch = sp.WaveFormat.Channels;
                var all = new List<float>();
                var tmp = new float[Rate * ch];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);

                if (ch == 1) return all.ToArray();
                var mono = new float[all.Count / ch];
                for (int f = 0; f < mono.Length; f++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += all[f * ch + c];
                    mono[f] = s / ch;
                }
                return mono;
            }
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null;
            _mixer = null;
        }

        private sealed class Voice
        {
            public float[] Buffer;   // shared mono buffer (read-only)
            public int Pos;          // per-voice playback position (loops)
            public float Volume;
            public float L = 0.70710677f, R = 0.70710677f; // constant-power pan gains
        }

        // Sums an arbitrary, changing set of looping mono voices into stereo. Sync (game thread) and Read
        // (audio thread) are both guarded by one lock — short critical sections, negligible contention.
        private sealed class Mixer : ISampleProvider
        {
            private readonly Dictionary<object, Voice> _voices = new Dictionary<object, Voice>();
            private readonly HashSet<object> _seen = new HashSet<object>();
            private readonly List<object> _remove = new List<object>();
            private readonly object _lock = new object();

            public WaveFormat WaveFormat { get; }
            public Mixer(int rate) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);

            public void Sync(List<VoiceSpec> specs, Func<string, float[]> getBuffer)
            {
                lock (_lock)
                {
                    _seen.Clear();
                    for (int i = 0; i < specs.Count; i++)
                    {
                        var s = specs[i];
                        if (!_voices.TryGetValue(s.Key, out var v))
                        {
                            v = new Voice { Buffer = getBuffer(s.Path) };
                            _voices[s.Key] = v;
                        }
                        else
                        {
                            var buf = getBuffer(s.Path);
                            if (v.Buffer != buf) { v.Buffer = buf; v.Pos = 0; } // sound changed (rare)
                        }
                        v.Volume = s.Volume;
                        float t = (Clamp(s.Pan, -1f, 1f) + 1f) * 0.5f * (float)(Math.PI / 2.0);
                        v.L = (float)Math.Cos(t);
                        v.R = (float)Math.Sin(t);
                        _seen.Add(s.Key);
                    }
                    _remove.Clear();
                    foreach (var k in _voices.Keys) if (!_seen.Contains(k)) _remove.Add(k);
                    for (int i = 0; i < _remove.Count; i++) _voices.Remove(_remove[i]);
                }
            }

            public void ClearVoices() { lock (_lock) _voices.Clear(); }

            public int Read(float[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++) buffer[offset + i] = 0f;
                int frames = count / 2;
                lock (_lock)
                {
                    foreach (var v in _voices.Values)
                    {
                        var buf = v.Buffer;
                        int len = buf?.Length ?? 0;
                        if (len == 0) continue;
                        float vol = v.Volume, l = v.L, r = v.R;
                        int pos = v.Pos;
                        for (int f = 0; f < frames; f++)
                        {
                            float s = buf[pos] * vol;
                            if (++pos >= len) pos = 0;
                            buffer[offset + f * 2] += s * l;
                            buffer[offset + f * 2 + 1] += s * r;
                        }
                        v.Pos = pos;
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    float x = buffer[offset + i];
                    buffer[offset + i] = x > 1f ? 1f : (x < -1f ? -1f : x);
                }
                return count;
            }

            private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
