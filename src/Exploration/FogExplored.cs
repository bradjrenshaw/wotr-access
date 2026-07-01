using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Collections;
using UnityEngine;
using FogArea = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.FogOfWarArea;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Tracks EXPLORED (ever-seen) terrain per area by accumulating the game's fog-of-war visibility over time.
    ///
    /// WotR keeps no persistent explored layer: <c>FogOfWarArea.FogOfWarMapRT</c> is CURRENT visibility (it
    /// re-fogs when the party leaves — verified live), and the local map just re-renders the full geometry with
    /// discovery-gated markers, so there's nothing to read back for "have I been here". We build it ourselves:
    /// every so often we snapshot that visibility texture (downsampled) and OR the lit cells into our own grid,
    /// which then answers <see cref="IsExplored"/> for any world point. In-session only — no save persistence
    /// yet (Phase 1); revisiting an area within the session keeps its grid via <see cref="_grids"/>.
    ///
    /// Cheap and alloc-free per tick: one GPU blit to a small scratch RT, a synchronous read of that, and a
    /// scan of ~64k cells OR'd into a reused byte grid. Coordinates map like the fog camera — a plain ortho
    /// projection over <c>GetWorldBounds()</c>, +x east / +z north, which we validated against the live texture.
    /// </summary>
    internal static class FogExplored
    {
        private const int N = 256;              // our explored grid resolution per axis (cell ≈ boundsSize/256)
        private const byte Threshold = 128;     // R >= this (0.5) = currently visible → mark the cell explored
        private const float IntervalSec = 0.5f; // how often we snapshot the fog (party vision changes slowly)

        private static readonly Dictionary<string, bool[]> _grids = new Dictionary<string, bool[]>();
        private static bool[] _grid;            // current area's grid (N*N, row-major, +z/north = increasing row)
        private static string _key;             // current fog area key (scene name)
        private static Bounds _bounds;          // current fog area world bounds
        private static bool _ready;             // current area has at least one snapshot
        private static float _timer;

        private static RenderTexture _small;    // NxN scratch for the downsample blit
        private static Texture2D _read;         // NxN CPU-side readback target

        /// <summary>Snapshot the fog and fold it into the current area's explored grid. Call once per frame.</summary>
        public static void Tick(float dt)
        {
            var fog = FogArea.Active;
            if (fog == null) return;
            var rt = fog.FogOfWarMapRT;
            if (rt == null) return;

            string key = fog.gameObject.scene.name;
            if (string.IsNullOrEmpty(key)) key = fog.name;
            if (key != _key)
            {
                _key = key;
                _bounds = fog.GetWorldBounds();
                if (!_grids.TryGetValue(key, out _grid)) { _grid = new bool[N * N]; _grids[key] = _grid; }
                _ready = false;
                _timer = 0f; // snapshot promptly on entering the area
            }

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = IntervalSec;
            Snapshot(rt);
        }

        private static void Snapshot(RenderTexture rt)
        {
            if (_small == null) { _small = new RenderTexture(N, N, 0, RenderTextureFormat.ARGB32); _small.Create(); }
            if (_read == null) _read = new Texture2D(N, N, TextureFormat.RGBA32, false);

            var prev = RenderTexture.active;
            Graphics.Blit(rt, _small);                     // GPU downsample the fog RT to NxN
            RenderTexture.active = _small;
            _read.ReadPixels(new Rect(0, 0, N, N), 0, 0);  // no Apply(): we only read the CPU copy back
            RenderTexture.active = prev;

            NativeArray<Color32> raw = _read.GetRawTextureData<Color32>(); // view, no managed allocation
            var g = _grid;
            int lim = Mathf.Min(raw.Length, g.Length);
            for (int i = 0; i < lim; i++)
                if (!g[i] && raw[i].r >= Threshold) g[i] = true;
            _ready = true;
        }

        /// <summary>Has this world point ever been revealed this session? Defaults to <c>true</c> when we have
        /// no data or the point is outside the fog area, so we never falsely report explored ground as unexplored.</summary>
        public static bool IsExplored(Vector3 world)
        {
            var g = _grid;
            if (!_ready || g == null) return true;
            var b = _bounds;
            if (b.size.x < 1e-3f || b.size.z < 1e-3f) return true;
            float u = (world.x - b.min.x) / b.size.x;
            float v = (world.z - b.min.z) / b.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return true; // outside the fog area — don't claim unexplored
            int x = Mathf.Clamp((int)(u * N), 0, N - 1);
            int y = Mathf.Clamp((int)(v * N), 0, N - 1);
            return g[y * N + x];
        }

        // ---- save persistence (Phase 2) ----
        // The per-area grids serialize to one Deflate'd blob that the save-hook stores as a named entry inside
        // the .zks (see FogPersistencePatch): [version][areaCount] then per area [sceneKey][gridN][bitpacked
        // grid]. Contiguous explored regions bit-pack + deflate to a few hundred bytes/area.

        private const byte Version = 1;

        /// <summary>Encode every accumulated area grid, or null when there's nothing to persist.</summary>
        public static byte[] Serialize()
        {
            if (_grids.Count == 0) return null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
                    using (var w = new BinaryWriter(ds))
                    {
                        w.Write(Version);
                        w.Write(_grids.Count);
                        foreach (var kv in _grids)
                        {
                            w.Write(kv.Key);
                            w.Write((ushort)N);
                            var g = kv.Value;
                            var packed = new byte[(g.Length + 7) / 8];
                            for (int i = 0; i < g.Length; i++)
                                if (g[i]) packed[i >> 3] |= (byte)(1 << (i & 7));
                            w.Write(packed.Length);
                            w.Write(packed);
                        }
                    }
                    return ms.ToArray();
                }
            }
            catch (Exception e) { Main.Log?.Error("[fog] serialize: " + e); return null; }
        }

        /// <summary>Replace all in-memory grids with the saved blob (clears first; a null/empty blob just clears).
        /// Resets the active binding so the next tick re-binds to the restored grid for the current area.</summary>
        public static void Restore(byte[] blob)
        {
            Clear();
            if (blob == null || blob.Length == 0) return;
            try
            {
                using (var ms = new MemoryStream(blob))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var r = new BinaryReader(ds))
                {
                    if (r.ReadByte() != Version) return;
                    int count = r.ReadInt32();
                    for (int a = 0; a < count; a++)
                    {
                        string key = r.ReadString();
                        int gn = r.ReadUInt16();
                        int packedLen = r.ReadInt32();
                        var packed = r.ReadBytes(packedLen);
                        if (gn != N) continue; // different resolution (future version) — drop this area
                        var g = new bool[N * N];
                        int cells = Math.Min(g.Length, packedLen * 8);
                        for (int i = 0; i < cells; i++)
                            if ((packed[i >> 3] & (1 << (i & 7))) != 0) g[i] = true;
                        _grids[key] = g;
                    }
                }
            }
            catch (Exception e) { Main.Log?.Error("[fog] restore: " + e); Clear(); }
        }

        /// <summary>Drop all accumulated explored data (new game, or before restoring another save).</summary>
        public static void Clear()
        {
            _grids.Clear();
            _grid = null;
            _key = null;
            _ready = false;
        }
    }
}
