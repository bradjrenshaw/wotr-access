using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Our own stereo audio backend (the "classic" engine). ONE shared <see cref="MixingSampleProvider"/>
    /// feeding ONE <see cref="WaveOutEvent"/> — every voice (wall tones now; one-shots/sources as they
    /// migrate) is an input on that single mixer, replacing the scattered per-consumer SfxPlayer /
    /// WallToneEngine instances that each opened their own device + feeder thread + buffer.
    /// </summary>
    internal sealed class NAudioEngine : IAudioEngine, IDisposable
    {
        public const int Rate = 44100; // mixer format; the wall-tone WAVs are authored at this rate

        private MixingSampleProvider _mixer;
        private IWavePlayer _out;

        public bool Available => true; // the default output device is always there

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            // 100 ms buffer to ride through managed-thread (GC/CPU) pauses without underrunning — see the
            // wall-tone/GC findings; below a full-GC pause and brief silences drop into continuous tones.
            _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        internal void Add(ISampleProvider p) { EnsureStarted(); _mixer.AddMixerInput(p); }
        internal void Remove(ISampleProvider p) { try { _mixer?.RemoveMixerInput(p); } catch { } }

        public IWallTones CreateWallTones(string toneSet)
        {
            EnsureStarted();
            var dir = Path.Combine(WrathAccess.Exploration.Overlays.OverlayAudio.Dir, "walltones", toneSet);
            return new WallTones(this, dir);
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null; _mixer = null;
        }

        // One-shots: decode the file once (cached), then add a self-removing OneShot voice to the shared mixer.
        // stem/worldPos are the Wwise inputs and ignored here; NAudio plays the file at (volume, pan).
        private readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        /// <summary>Non-positional (stereo-centred) one-shot — UI/cue sounds, on the same shared output.</summary>
        public void Play2D(string file, float volume) => PlayOneShot(null, file, Vector3.zero, volume, 0f);

        public void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan)
        {
            try
            {
                EnsureStarted();
                var buf = Get(file);
                if (buf != null && buf.Length > 0) _mixer.AddMixerInput(new OneShot(buf, Rate, volume, pan));
            }
            catch (Exception e) { Main.Log?.Error("[naudio] one-shot " + file + " — " + e); }
        }

        private float[] Get(string path)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var buf = Decode(path);
            _cache[path] = buf;
            return buf;
        }

        // Decode a WAV, normalised to the mixer format (44.1 kHz stereo float). Ported from SfxPlayer.
        private static float[] Decode(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                ISampleProvider sp = reader;
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                var all = new List<float>(Rate);
                var tmp = new float[Rate * 2];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        // Plays a cached interleaved-stereo buffer once with a constant-power pan; returns < count at the end
        // so the mixer auto-removes it. Ported verbatim from SfxPlayer.OneShot.
        private sealed class OneShot : ISampleProvider
        {
            private readonly float[] _buf;
            private readonly float _gainL, _gainR;
            private int _pos;

            public OneShot(float[] buf, int rate, float vol, float pan)
            {
                _buf = buf;
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

        // Four looping mono channels summed to stereo with a fixed constant-power pan (E/W hard right/left,
        // N/S centred), added as ONE input to the shared mixer. Ported verbatim from WallToneEngine.Mixer
        // so the pan amounts + loop wraparound are byte-for-byte identical — only the output is now shared.
        private sealed class WallTones : ISampleProvider, IWallTones
        {
            private sealed class Channel
            {
                public float[] Buffer = Array.Empty<float>();
                public int Pos;
                public volatile float Volume;
                public float LeftGain = 0.70710677f;
                public float RightGain = 0.70710677f;
            }

            private readonly Channel[] _channels = { new Channel(), new Channel(), new Channel(), new Channel() };
            private readonly NAudioEngine _engine;
            public WaveFormat WaveFormat { get; }

            public WallTones(NAudioEngine engine, string setDir)
            {
                _engine = engine;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
                Set(0, Path.Combine(setDir, "north.wav"), 0f);
                Set(1, Path.Combine(setDir, "south.wav"), 0f);
                Set(2, Path.Combine(setDir, "east.wav"), 1f);  // hard right
                Set(3, Path.Combine(setDir, "west.wav"), -1f); // hard left
                engine.Add(this);
            }

            private void Set(int i, string path, float pan)
            {
                var c = _channels[i];
                try { c.Buffer = ReadMono(path); } catch (Exception e) { Main.Log?.Error("[walltones] load " + path + " — " + e.Message); c.Buffer = Array.Empty<float>(); }
                c.Pos = 0; c.Volume = 0f;
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0); // -1=L, +1=R, 0=centred (0.707 each)
                c.LeftGain = (float)Math.Cos(t);
                c.RightGain = (float)Math.Sin(t);
            }

            // hits unused on this engine (pan is fixed per direction); volumes drive the four channels.
            public void Update(Vector3[] hits, float[] volumes)
            {
                for (int i = 0; i < _channels.Length && i < volumes.Length; i++)
                {
                    float v = volumes[i];
                    _channels[i].Volume = v < 0f ? 0f : (v > 1f ? 1f : v);
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                for (int f = 0; f < frames; f++)
                {
                    float l = 0f, r = 0f;
                    for (int i = 0; i < _channels.Length; i++)
                    {
                        var c = _channels[i];
                        int len = c.Buffer.Length;
                        if (len == 0) continue;
                        float s = c.Buffer[c.Pos] * c.Volume;
                        c.Pos++;
                        if (c.Pos >= len) c.Pos = 0; // seamless wrap
                        l += s * c.LeftGain;
                        r += s * c.RightGain;
                    }
                    buffer[offset + f * 2] = l > 1f ? 1f : (l < -1f ? -1f : l);
                    buffer[offset + f * 2 + 1] = r > 1f ? 1f : (r < -1f ? -1f : r);
                }
                return count; // ReadFully mixer: always full (silence when all volumes are 0)
            }

            public void Dispose() => _engine.Remove(this);

            // Read a WAV fully into a mono float[] (averaging channels). WAVs are authored at Rate.
            private static float[] ReadMono(string path)
            {
                using (var reader = new AudioFileReader(path))
                {
                    int ch = reader.WaveFormat.Channels;
                    var all = new List<float>(reader.WaveFormat.SampleRate * ch);
                    var buf = new float[reader.WaveFormat.SampleRate * ch];
                    int read;
                    while ((read = reader.Read(buf, 0, buf.Length)) > 0)
                        for (int i = 0; i < read; i++) all.Add(buf[i]);
                    int frames = all.Count / ch;
                    var mono = new float[frames];
                    for (int fr = 0; fr < frames; fr++)
                    {
                        float s = 0f;
                        for (int c = 0; c < ch; c++) s += all[fr * ch + c];
                        mono[fr] = s / ch;
                    }
                    return mono;
                }
            }
        }
    }
}
