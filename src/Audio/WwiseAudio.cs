using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Kingmaker.Sound;              // DefaultListener (volume scaling target)
using Owlcat.Runtime.Core.Registry; // ObjectRegistry
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Plays the mod's wavs as REAL Wwise 3D emitters — the same audio engine, listener (the virtual
    /// head), distance attenuation, and mixer buses as the game's own sounds, so sonification and
    /// game audio share one spatial frame. A soundbank is generated at boot from every wav under
    /// assets/audio (<see cref="WwiseBank"/>) and loaded in-memory; each wav is postable as event
    /// "wa_&lt;stem&gt;" on a pooled, positioned emitter object. Per-call volume rides
    /// SetGameObjectOutputBusVolume (emitter→listener), so the mod's volume settings apply without
    /// bank-side RTPCs. If the bank fails to load, <see cref="TryPost"/> returns false and callers
    /// fall back to the classic Unity path.
    /// </summary>
    internal static class WwiseAudio
    {
        private const int PoolSize = 8;

        private static bool _attempted;
        private static bool _ready;
        private static readonly List<GameObject> _pool = new List<GameObject>();
        private static int _next;

        public static bool Ready => _ready;

        private static bool Enabled =>
            ModSettings.GetSetting<ChoiceSetting>("audio.engine")?.ValueId != "classic";

        /// <summary>Lazy init: the Wwise engine comes up during game boot; load our bank once it's there.</summary>
        public static void Tick()
        {
            if (_attempted) return;
            if (!AkSoundEngine.IsInitialized()) return;
            _attempted = true;
            try
            {
                Load();
            }
            catch (Exception e)
            {
                Main.Log?.Error("[wwise] bank load failed: " + e);
                _ready = false;
            }
        }

        private static void Load()
        {
            var wavs = new List<KeyValuePair<string, byte[]>>();
            var seen = new HashSet<string>();
            var dir = Exploration.Overlays.OverlayAudio.Dir;
            if (!Directory.Exists(dir)) { Main.Log?.Warning("[wwise] no audio dir: " + dir); return; }
            foreach (var f in Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories))
            {
                var stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (!seen.Add(stem)) continue; // stems are the identity; first wins on duplicates
                wavs.Add(new KeyValuePair<string, byte[]>(stem, File.ReadAllBytes(f)));
            }
            if (wavs.Count == 0) return;

            var bank = WwiseBank.Build(wavs, "wrathaccess", out uint bankId);
            var handle = GCHandle.Alloc(bank, GCHandleType.Pinned);
            try
            {
                // MemoryCopy: Wwise takes its own copy, the managed array can be collected after.
                var result = AkSoundEngine.LoadBankMemoryCopy(handle.AddrOfPinnedObject(), (uint)bank.Length, out uint loadedId);
                if (result != AKRESULT.AK_Success)
                {
                    Main.Log?.Error("[wwise] LoadBankMemoryCopy: " + result);
                    return;
                }
                Main.Log?.Log("[wwise] bank loaded: " + wavs.Count + " sounds, " + bank.Length + " bytes, id " + loadedId);
            }
            finally { handle.Free(); }

            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("WrathAccess.Emitter" + i);
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<AkGameObj>(); // registers with Wwise and tracks the transform
                _pool.Add(go);
            }
            _ready = true;
        }

        /// <summary>Post the wav's event at a world position with a 0..1 volume. Returns false when
        /// the Wwise path isn't available/enabled — caller falls back to the classic path.</summary>
        public static bool TryPost(string stem, Vector3 position, float volume)
        {
            if (!_ready || !Enabled || string.IsNullOrEmpty(stem)) return false;
            var go = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            go.transform.position = position;
            // Push the position explicitly — AkGameObj syncs in its own Update, which may run after us.
            AkSoundEngine.SetObjectPosition(go, position, Vector3.forward, Vector3.up);

            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener != null)
                AkSoundEngine.SetGameObjectOutputBusVolume(go, listener.gameObject, Mathf.Clamp01(volume));

            uint playing = AkSoundEngine.PostEvent("wa_" + stem.ToLowerInvariant(), go);
            // Kick the audio thread NOW instead of waiting for the integration's end-of-frame
            // RenderAudio — posted events otherwise sit in the queue for up to a whole frame,
            // which reads as audible lag next to the old direct-to-mixer path.
            if (playing != 0) AkSoundEngine.RenderAudio();
            return playing != 0; // 0 = invalid event/unloaded bank → let the caller fall back
        }
    }
}
