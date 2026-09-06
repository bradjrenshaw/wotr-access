using System;
using System.Collections.Generic;
using System.IO;
using Loadstar;
using Loadstar.Audio;
using NAudio.Wave;
using UnityEngine;
using WrathAccess.Settings;
using WrathAccess.Speech; // SpeechAudio
using Vec3 = Loadstar.Vec3;
using Bounds = Loadstar.Bounds;
using Vector3 = UnityEngine.Vector3;

namespace WrathAccess.Audio
{
    /// <summary>
    /// The LOW-LATENCY backend: the user's Loadstar native library (<c>loadstar.dll</c> next to
    /// Wrath.exe, Rust). Its WASAPI backend mixes on its own MMCSS "Pro Audio" thread at the driver's
    /// minimum <c>IAudioClient3</c> period (~2.7 ms at 48 kHz) — a native thread Mono's GC world-stops
    /// can't pause, and the crate never calls back into managed code, so the 50 ms WaveOut cushion
    /// the NAudio engine needs against those pauses goes away. Our consumer surface stays the same
    /// (<see cref="IAudioEngine"/>); what changes underneath:
    /// <list type="bullet">
    ///  <item>ONE planar space with the listener pinned at the origin facing north: every caller
    ///   already hands us listener-relative, facing-rotated offsets (dxEast/dzNorth), so sources are
    ///   simply placed at those offsets and the crate's head model (the port of our
    ///   <see cref="Spatializer"/>: capped ILD, ITD, far-ear shadow, rear shelf) does the panning.
    ///   The mod's cue toggles map onto the space-level spatializer node's flag bits.</item>
    ///  <item>Wall tones = four looping buffer sources parked 1 m N/S/E/W of the listener (south
    ///   gets the rear shelf, east/west the ITD, exactly like the NAudio bank's fixed pans), gains
    ///   upserted per frame — the crate ramps them, so no clicks.</item>
    ///  <item>Tracked one-shots (<see cref="SpatialSources"/>) = persistent sources whose bounds/gain
    ///   are upserted per frame via <see cref="ISpatialVoice.SetOffset"/>; the crate re-spatialises
    ///   every period. Removed once the finished poll reports them (NOT AutoRemove: a per-frame
    ///   upsert after auto-removal would recreate and replay the source).</item>
    ///  <item>Speech PCM = a buffer uploaded per utterance, fired relative with the pan, freed after
    ///   the finished poll. UI one-shots = anonymous relative fires on cached buffers.</item>
    /// </list>
    /// Buffers are uploaded once at their native rate/channels — the crate resamples at upload.
    /// Opening can fail (no loadstar.dll, no device): <see cref="TryOpen"/> returns null and
    /// <see cref="AudioEngines"/> falls back to NAudio, logging why.
    /// </summary>
    internal sealed class LoadstarEngine : IAudioEngine
    {
        private readonly AudioDevice _dev;
        private readonly uint _space;
        private readonly uint _busWalls, _busCues, _busSpeech, _busUi;
        private readonly Dictionary<string, uint> _buffers = new Dictionary<string, uint>();
        private readonly Dictionary<ulong, LoadstarVoice> _voices = new Dictionary<ulong, LoadstarVoice>();
        private readonly Dictionary<ulong, uint> _speechBuffers = new Dictionary<ulong, uint>(); // fired speech → buffer to free
        private ulong _nextId = 1;
        private EffectFlags _cueFlags;
        private float _nextSettingsPoll;
        private bool _disposed;

        public string Description { get; }

        private LoadstarEngine(AudioDevice dev)
        {
            _dev = dev;
            _space = dev.CreateSpace(SpaceMode.Planar, 1f, 1f);
            // Loadstar is right-handed (right = forward × up): its "north" is −Z, so the listener
            // faces −Z and every Unity offset (dx east, dz north) is placed at (dx, 0, −dz). Facing +Z
            // here put east in the LEFT ear (the swapped-wall-tones repro).
            dev.SetListener(_space, new Vec3(0, 0, 0), new Vec3(0, 0, -1), new Vec3(0, 1, 0));
            _busWalls = dev.CreateBus(_space);
            _busCues = dev.CreateBus(_space);
            _busSpeech = dev.CreateBus(_space);
            _busUi = dev.CreateBus(_space);
            _cueFlags = CueFlags();
            dev.SetChain(ChainTarget.ForSpace(_space), SpaceChain(_cueFlags));
            Description = dev.Info.ToString();
        }

        /// <summary>Open the default output through loadstar.dll; null (logged) when the library or a
        /// device isn't there, so the caller can fall back.</summary>
        public static LoadstarEngine TryOpen()
        {
            try
            {
                var dev = AudioDevice.Open(new DeviceConfig { MasterGain = 1f });
                var e = new LoadstarEngine(dev);
                Main.Log?.Log("[loadstar] output open: " + e.Description);
                return e;
            }
            catch (DllNotFoundException e) { Main.Log?.Warning("[loadstar] loadstar.dll not found next to Wrath.exe — " + e.Message); }
            catch (EntryPointNotFoundException e) { Main.Log?.Warning("[loadstar] loadstar.dll is too old (missing export) — " + e.Message); }
            catch (Exception e) { Main.Log?.Warning("[loadstar] open failed — " + e.Message); }
            return null;
        }

        // The space chain: the head model with the mod's cue flags, plus "no distance model" — every
        // caller computes its own distance → gain curve and hands it over as the source gain (the
        // NAudio path never attenuated by distance either), so the crate's implicit inverse falloff
        // (1/d — near-silent past a few metres) must be switched off. Model NONE + the space-level
        // fallback are loadstar changes made for this (2026-09-06); an OLDER loadstar.dll ignores both
        // (it only reads a source's own pre chain and maps unknown models to inverse), which the
        // per-source node below covers for tracked voices.
        private static EffectDesc[] SpaceChain(EffectFlags cues) => new[]
        {
            EffectDesc.Spatializer(cues),
            EffectDesc.Distance(1f, 0f, 0f, DistanceModel.None),
        };

        // Per-source "no distance model" that ALSO works on an older loadstar.dll: rolloff 0 makes
        // the inverse law (min/d)^0 = 1 at every distance, no cutoff.
        private static readonly EffectDesc[] NoDistanceChain = { EffectDesc.Distance(1f, 0f, 0f, DistanceModel.Inverse) };

        // The mod's A/B-able cue toggles (Audio tab) → the head model's flag bits.
        private static EffectFlags CueFlags()
        {
            var f = EffectFlags.Enabled;
            if (Spatializer.ItdEnabled) f |= EffectFlags.Itd;
            if (Spatializer.ShadowEnabled) f |= EffectFlags.HeadShadow;
            if (Spatializer.FilterEnabled) f |= EffectFlags.RearCue;
            return f;
        }

        public void Tick()
        {
            if (_disposed) return;
            try
            {
                // Finished sequences: drop tracked voices (and their sources), free speech buffers.
                var done = _dev.TakeFinished();
                for (int i = 0; i < done.Length; i++)
                {
                    ulong id = done[i].Id;
                    if (_voices.TryGetValue(id, out var v))
                    {
                        v.Finished = true;
                        _voices.Remove(id);
                        try { _dev.Remove(_space, id); } catch { }
                    }
                    if (_speechBuffers.TryGetValue(id, out uint buf))
                    {
                        _speechBuffers.Remove(id);
                        try { _dev.FreeBuffer(buf); } catch { }
                    }
                }
                // Cue toggles are settings; re-apply the space's spatializer node when they change.
                float now = Time.unscaledTime;
                if (now >= _nextSettingsPoll)
                {
                    _nextSettingsPoll = now + 0.5f;
                    var f = CueFlags();
                    if (f != _cueFlags)
                    {
                        _cueFlags = f;
                        _dev.SetChain(ChainTarget.ForSpace(_space), SpaceChain(f));
                    }
                }
            }
            catch (Exception e) { Main.Log?.Error("[loadstar] tick — " + e.Message); }
        }

        // ---- one-shots ----

        public void Play2D(string file, float volume) => PlayOneShot(null, file, Vector3.zero, volume, 0f);

        public void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan)
        {
            if (_disposed) return;
            try
            {
                if (!TryBuffer(file, out uint buf)) return;
                var d = SourceDesc.Buffer_(0, buf, Bounds.Point(new Vec3(0, 0, 0)))
                    .Relative(Mathf.Clamp(pan, -1f, 1f)).OnBus(_busUi).WithGain(Mathf.Max(0f, volume));
                _dev.Fire(_space, d);
            }
            catch (Exception e) { Main.Log?.Error("[loadstar] one-shot " + file + " — " + e.Message); }
        }

        public void PlayPcm(SpeechAudio audio, float volume, float pan)
        {
            if (_disposed || audio?.Pcm == null || audio.Pcm.Length == 0) return;
            try
            {
                var samples = DecodePcm(audio, out int channels);
                if (samples == null || samples.Length == 0) return;
                uint buf = _dev.CreateBuffer(samples, (uint)channels, (uint)audio.SampleRate);
                ulong id = _nextId++;
                _speechBuffers[id] = buf; // freed when the finished poll reports this id
                var d = SourceDesc.Buffer_(id, buf, Bounds.Point(new Vec3(0, 0, 0)))
                    .Relative(Mathf.Clamp(pan, -1f, 1f)).OnBus(_busSpeech).WithGain(Mathf.Max(0f, volume * audio.Gain));
                _dev.Fire(_space, d);
            }
            catch (Exception e) { Main.Log?.Error("[loadstar] speech — " + e.Message); }
        }

        public ISpatialVoice PlaySpatial(string file, float volume, float dxEast, float dzNorth, float panWidth)
        {
            if (_disposed) return null;
            try
            {
                if (!TryBuffer(file, out uint buf)) return null;
                ulong id = _nextId++;
                var voice = new LoadstarVoice(this, id, buf, _busCues);
                voice.SetOffset(dxEast, dzNorth, panWidth, volume); // the upsert creates the source...
                _dev.SetChain(ChainTarget.SourcePre(_space, id), NoDistanceChain); // ...then its pre chain
                _voices[id] = voice;
                return voice;
            }
            catch (Exception e) { Main.Log?.Error("[loadstar] spatial " + file + " — " + e.Message); return null; }
        }

        private void Upsert(in SourceDesc d)
        {
            if (_disposed) return;
            _dev.Upsert(_space, d);
        }

        /// <summary>A tracked positional one-shot: one persistent source, re-placed by
        /// <see cref="SetOffset"/> each frame (the crate re-spatialises it every period). The cue-based
        /// <see cref="SetPlacement"/> is a no-op here — the head model owns those cues.</summary>
        private sealed class LoadstarVoice : ISpatialVoice
        {
            private readonly LoadstarEngine _engine;
            private SourceDesc _desc;
            public bool Finished { get; set; }

            public LoadstarVoice(LoadstarEngine engine, ulong id, uint buffer, uint bus)
            {
                _engine = engine;
                _desc = SourceDesc.Buffer_(id, buffer, Bounds.Point(new Vec3(0, 0, 0))).OnBus(bus);
            }

            public void SetPlacement(SpatialCue cue, float volume) { }

            public void SetOffset(float dxEast, float dzNorth, float panWidth, float volume)
            {
                if (Finished) return;
                _desc.Bounds = Bounds.Point(At(dxEast, dzNorth));
                _desc.PanWidth = Mathf.Max(0.01f, panWidth);
                _desc.Gain = Mathf.Max(0f, volume);
                _engine.Upsert(_desc);
            }
        }

        // ---- wall tones ----

        public IWallTones CreateWallTones(string toneSet)
        {
            var dir = Path.Combine(WrathAccess.Exploration.Overlays.OverlayAudio.Dir, "walltones", toneSet);
            return new LoadstarWallTones(this, dir, toneSet);
        }

        /// <summary>Four looping sources 1 m ahead / behind / right / left of the listener on the walls
        /// bus — the head model gives behind its rear shelf and the sides their ITD, matching the
        /// NAudio bank's fixed ear-space pans. Per-frame gains via upsert (ramped in the crate).</summary>
        private sealed class LoadstarWallTones : IWallTones
        {
            private static readonly float[] TrimDbSet1 = { 0f, 7.7f, 2.7f, 2.7f };   // north, south, east, west (see NAudioEngine)
            private static readonly float[] TrimDbSet2 = { -3.8f, 1.0f, -2.7f, -2.7f };
            private static readonly string[] Files = { "north.wav", "south.wav", "east.wav", "west.wav" };
            // ahead, behind, right, left — through At(): +z north in Unity terms.
            private static readonly Vec3[] Places = { At(0, 1), At(0, -1), At(1, 0), At(-1, 0) };
            private readonly LoadstarEngine _engine;
            private readonly SourceDesc[] _descs = new SourceDesc[4];
            private readonly float[] _trim = new float[4];
            private readonly float[] _last = { -1f, -1f, -1f, -1f };
            private readonly bool[] _loaded = new bool[4];
            private bool _disposed;

            public LoadstarWallTones(LoadstarEngine engine, string dir, string set)
            {
                _engine = engine;
                var trims = set == "2" ? TrimDbSet2 : TrimDbSet1;
                for (int i = 0; i < 4; i++)
                {
                    _trim[i] = (float)Math.Pow(10.0, trims[i] / 20.0);
                    _loaded[i] = engine.TryBuffer(Path.Combine(dir, Files[i]), out uint buf);
                    ulong id = engine._nextId++;
                    _descs[i] = SourceDesc.Buffer_(id, buf, Bounds.Point(Places[i])).Looping().OnBus(engine._busWalls).WithGain(0f);
                    _descs[i].PanWidth = 1f; // the side sources sit exactly one crossover out: full pan
                    if (_loaded[i]) engine.Upsert(_descs[i]);
                }
            }

            public void Update(Vector3[] hits, float[] volumes)
            {
                if (_disposed) return;
                for (int i = 0; i < 4 && i < volumes.Length; i++)
                {
                    if (!_loaded[i]) continue;
                    float v = volumes[i];
                    float g = (v < 0f ? 0f : (v > 1f ? 1f : v)) * _trim[i];
                    if (Math.Abs(g - _last[i]) < 0.002f) continue; // unchanged: no command
                    _last[i] = g;
                    _descs[i].Gain = g;
                    _engine.Upsert(_descs[i]);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                for (int i = 0; i < 4; i++)
                    try { if (!_engine._disposed) _engine._dev.Remove(_engine._space, _descs[i].Id); } catch { }
            }
        }

        /// <summary>A Unity-frame offset (metres east, metres north) as a loadstar position for the
        /// origin-pinned listener facing the crate's north (−Z).</summary>
        private static Vec3 At(float dxEast, float dzNorth) => new Vec3(dxEast, 0f, -dzNorth);

        // ---- buffers ----

        // Buffer ids are assigned by the crate FROM ZERO (north.wav landed on id 0 and a "0 = failed"
        // sentinel silenced it), so load failures are tracked separately, not by id.
        private readonly HashSet<string> _failed = new HashSet<string>();

        /// <summary>The uploaded buffer for a WAV path (cached). False when the file can't be read
        /// (logged once; the path is remembered as failed).</summary>
        private bool TryBuffer(string path, out uint id)
        {
            if (_buffers.TryGetValue(path, out id)) return true;
            id = 0;
            if (_failed.Contains(path)) return false;
            try
            {
                var samples = LoadWav(path, out int channels, out int rate);
                if (samples != null && samples.Length > 0)
                {
                    id = _dev.CreateBuffer(samples, (uint)channels, (uint)rate);
                    _buffers[path] = id;
                    return true;
                }
                Main.Log?.Error("[loadstar] load " + path + " — empty");
            }
            catch (Exception e) { Main.Log?.Error("[loadstar] load " + path + " — " + e.Message); }
            _failed.Add(path);
            return false;
        }

        // Interleaved float PCM at the file's own rate/channels — the crate converts at upload.
        private static float[] LoadWav(string path, out int channels, out int rate)
        {
            using (var reader = new AudioFileReader(path))
            {
                channels = reader.WaveFormat.Channels;
                rate = reader.WaveFormat.SampleRate;
                var all = new List<float>((int)Math.Min(int.MaxValue, reader.Length / 4 + 16));
                var tmp = new float[rate * channels];
                int n;
                while ((n = reader.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        private static float[] DecodePcm(SpeechAudio audio, out int channels)
        {
            channels = Math.Max(1, audio.Channels);
            var fmt = new WaveFormat(audio.SampleRate, audio.BitsPerSample, channels);
            using (var ms = new MemoryStream(audio.Pcm))
            using (var raw = new RawSourceWaveStream(ms, fmt))
            {
                var sp = raw.ToSampleProvider();
                var all = new List<float>(audio.Pcm.Length / 2 + 16);
                var tmp = new float[audio.SampleRate * channels];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _dev.Dispose(); } catch { }
        }
    }
}
