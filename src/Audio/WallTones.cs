using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Plays the four directional wall tones (north/south/east/west .wav) through NAudio, on the default
    /// output device — independent of the game's Wwise audio. Each direction is one looping channel whose
    /// volume we drive from the cursor's distance to a wall in that direction (closer = louder). East/west
    /// are panned hard right/left; north/south play centred and are told apart by being distinct sounds.
    ///
    /// The WAVs are folded to mono on load (NAudio's clean pan needs mono), then a single mixer sums the
    /// four channels to stereo. Volumes are set live each frame; the audio thread reads them (volatile),
    /// the game thread writes them — no locking needed for plain float reads/writes.
    /// </summary>
    internal sealed class WallToneEngine : IDisposable
    {
        private IWavePlayer _out;
        private Mixer _mixer;

        public bool IsLoaded => _mixer != null;

        /// <summary>Load a tone set from a folder containing north/south/east/west.wav and start playback
        /// (silent until volumes are set). Returns false (and logs) if anything's missing/unplayable.</summary>
        public bool Load(string setDir)
        {
            try
            {
                Dispose();
                var north = ReadMono(Path.Combine(setDir, "north.wav"), out int rate);
                var south = ReadMono(Path.Combine(setDir, "south.wav"), out _);
                var east = ReadMono(Path.Combine(setDir, "east.wav"), out _);
                var west = ReadMono(Path.Combine(setDir, "west.wav"), out _);

                _mixer = new Mixer(rate);
                _mixer.SetChannel(Dir.North, north, pan: 0f);
                _mixer.SetChannel(Dir.South, south, pan: 0f);
                _mixer.SetChannel(Dir.East, east, pan: 1f);   // hard right
                _mixer.SetChannel(Dir.West, west, pan: -1f);  // hard left

                // Buffer must ride through managed-thread (GC/CPU) pauses or they drop brief silences into the
                // continuous tone: a 50 ms buffer was smaller than a ~57 ms full-GC pause, so it underran. 100 ms
                // (matching SfxPlayer) clears that; the cost is volume tracks the cursor ~100 ms slower. Tunable,
                // but must stay above the worst managed pause or the gaps return.
                _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
                _out.Init(_mixer);
                _out.Play();
                return true;
            }
            catch (Exception e)
            {
                Main.Log?.Error("[walltones] load failed: " + e);
                Dispose();
                return false;
            }
        }

        /// <summary>Set each channel's volume (0..1) — typically (maxRange - wallDistance)/maxRange, curved.</summary>
        public void SetVolumes(float north, float south, float east, float west)
        {
            var m = _mixer;
            if (m == null) return;
            m.SetVolume(Dir.North, north);
            m.SetVolume(Dir.South, south);
            m.SetVolume(Dir.East, east);
            m.SetVolume(Dir.West, west);
        }

        public void Mute() => SetVolumes(0f, 0f, 0f, 0f);

        public void Dispose()
        {
            try { _out?.Stop(); } catch { }
            try { _out?.Dispose(); } catch { }
            _out = null;
            _mixer = null;
        }

        // Reads a WAV fully into a mono float[] (averaging channels). Out is the source sample rate.
        private static float[] ReadMono(string path, out int sampleRate)
        {
            using (var reader = new AudioFileReader(path))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                int ch = reader.WaveFormat.Channels;
                var all = new List<float>(reader.WaveFormat.SampleRate * ch);
                var buf = new float[reader.WaveFormat.SampleRate * ch];
                int read;
                while ((read = reader.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < read; i++) all.Add(buf[i]);

                int frames = all.Count / ch;
                var mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += all[f * ch + c];
                    mono[f] = s / ch;
                }
                return mono;
            }
        }

        private enum Dir { North, South, East, West }

        // Sums four looping mono channels into a stereo float stream, applying per-channel volume + a
        // fixed constant-power pan. Read runs on NAudio's audio thread.
        private sealed class Mixer : ISampleProvider
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

            public WaveFormat WaveFormat { get; }

            public Mixer(int sampleRate) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

            public void SetChannel(Dir dir, float[] buffer, float pan)
            {
                var c = _channels[(int)dir];
                c.Buffer = buffer ?? Array.Empty<float>();
                c.Pos = 0;
                c.Volume = 0f;
                // constant-power pan: pan -1 = full left, +1 = full right, 0 = centred (0.707 each)
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                c.LeftGain = (float)Math.Cos(t);
                c.RightGain = (float)Math.Sin(t);
            }

            public void SetVolume(Dir dir, float v) => _channels[(int)dir].Volume = v < 0f ? 0f : (v > 1f ? 1f : v);

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
                        if (c.Pos >= len) c.Pos = 0;
                        l += s * c.LeftGain;
                        r += s * c.RightGain;
                    }
                    buffer[offset + f * 2] = l > 1f ? 1f : (l < -1f ? -1f : l);
                    buffer[offset + f * 2 + 1] = r > 1f ? 1f : (r < -1f ? -1f : r);
                }
                return count;
            }
        }
    }
}
