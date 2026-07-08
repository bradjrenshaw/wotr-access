#if DEBUG
using System;
using System.Collections.Generic;
using Kingmaker;
using Newtonsoft.Json;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar;
using UnityEngine;
using WrathAccess.Exploration;

namespace WrathAccess.Dev
{
    /// <summary>
    /// The survey half of the environmental-descriptions authoring pipeline
    /// (docs/design/environmental-descriptions.md): tools/survey.py drives these methods through the
    /// dev server's /eval as one-liners, so all the logic lives HERE, compile-checked, with direct
    /// access to the mod's internals (RoomMap grid, WorldModel, EnvDescriptions) instead of fragile
    /// eval-string reflection. PUBLIC class because Mono.CSharp eval sees only public members
    /// (see <see cref="DevApi"/>); DEBUG-only like the rest of the dev subsystem.
    ///
    /// Every method returns a JSON string (the driver reads the "=> ..." eval result line).
    /// <see cref="Frame"/> saves camera + fog state on first use; <see cref="Restore"/> puts it back.
    /// </summary>
    public static class DevSurvey
    {
        // ---- read-only queries ----

        /// <summary>Current area: blueprint name (= the descriptions file name) + display name.</summary>
        public static string Area()
        {
            var area = Game.Instance != null ? Game.Instance.CurrentlyLoadedArea : null;
            if (area == null) return Err("no area loaded");
            return JsonConvert.SerializeObject(new
            {
                blueprint = area.name,
                display = TextUtil.StripRichText(area.AreaDisplayName),
            });
        }

        /// <summary>All RoomMap rooms with survey points: centroid-seeded farthest-point sampling over
        /// the room's own cells, count scaling with area (1 + area/200 m², capped), stopping early when
        /// no candidate is ≥3 m from the picked set (small rooms stay one-shot). Exits included so the
        /// authoring data can mention where openings lead.</summary>
        public static string Rooms(int maxPoints = 4)
        {
            if (!RoomMap.Ready || !RoomMap.TryGetGrid(out var label, out _, out int w, out int _))
                return Err("no room map (area still loading?)");

            var rooms = new List<object>();
            var cells = new List<int>();
            foreach (var room in RoomMap.Rooms)
            {
                int lbl = room.Id - 1; // ids are the label indices, 1-based
                cells.Clear();
                for (int i = 0; i < label.Length; i++) if (label[i] == lbl) cells.Add(i);
                if (cells.Count == 0) continue;

                int want = Mathf.Clamp(1 + (int)(room.Area / 200f), 1, maxPoints);
                var picked = new List<int>();
                int seed = cells[0]; float bd = float.MaxValue;
                foreach (var i in cells)
                {
                    var p = RoomMap.CellCenter(i % w, i / w);
                    float dx = p.x - room.Centroid.x, dz = p.z - room.Centroid.z, d = dx * dx + dz * dz;
                    if (d < bd) { bd = d; seed = i; }
                }
                picked.Add(seed);
                while (picked.Count < want)
                {
                    int best = -1; float bestMin = -1f;
                    foreach (var i in cells)
                    {
                        var p = RoomMap.CellCenter(i % w, i / w);
                        float mn = float.MaxValue;
                        foreach (var s in picked)
                        {
                            var q = RoomMap.CellCenter(s % w, s / w);
                            float dx = p.x - q.x, dz = p.z - q.z, d = dx * dx + dz * dz;
                            if (d < mn) mn = d;
                        }
                        if (mn > bestMin) { bestMin = mn; best = i; }
                    }
                    if (best < 0 || bestMin < 9f) break; // nothing ≥3 m from the picked set
                    picked.Add(best);
                }

                var pts = new List<object>();
                foreach (var i in picked)
                {
                    var p = RoomMap.CellCenter(i % w, i / w);
                    pts.Add(new { x = R(p.x), y = R(p.y), z = R(p.z) });
                }
                var exits = new List<object>();
                foreach (var e in room.Exits)
                    exits.Add(new { x = R(e.Position.x), y = R(e.Position.y), z = R(e.Position.z), to = e.To != null ? e.To.Id : 0 });
                rooms.Add(new
                {
                    id = room.Id,
                    cls = room.ClassKey,
                    area = R(room.Area),
                    cx = R(room.Centroid.x), cy = R(room.Centroid.y), cz = R(room.Centroid.z),
                    points = pts,
                    exits,
                });
            }
            return JsonConvert.SerializeObject(new { rooms });
        }

        /// <summary>A room's live contents, pre-categorized for the stage-not-actors rule: map objects
        /// (stable, describable, with normalized asset keys + whether the global table already has text)
        /// vs units (dynamic — LISTED so authoring can consciously exclude them, never described).</summary>
        public static string Contents(int roomId)
        {
            var objects = new List<object>();
            var units = new List<object>();
            foreach (var it in WorldModel.Items)
            {
                Vector3 p;
                string name;
                try { p = it.Position; name = it.Name; }
                catch { continue; } // a proxy mid-despawn shouldn't sink the dump
                var room = RoomMap.RoomAt(p);
                if (room == null || room.Id != roomId) continue;
                if (it.IsUnit) units.Add(new { name });
                else objects.Add(new
                {
                    name,
                    asset = it.AssetKey,
                    kind = it.Primary,
                    x = R(p.x), y = R(p.y), z = R(p.z),
                    described = EnvDescriptions.HasAssetText(it.AssetKey),
                });
            }
            return JsonConvert.SerializeObject(new { objects, units });
        }

        /// <summary>The area's unique describable assets: every distinct normalized asset key among
        /// non-unit scan items, with one representative instance (for framing a capture), an instance
        /// count, and whether _assets.json already covers it (the driver skips those by default).</summary>
        public static string Assets()
        {
            var seen = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in WorldModel.Items)
            {
                if (it.IsUnit) continue;
                string key;
                Vector3 p;
                string name;
                try { key = it.AssetKey; p = it.Position; name = it.Name; }
                catch { continue; }
                if (string.IsNullOrEmpty(key)) continue;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                if (!seen.ContainsKey(key))
                    seen[key] = new
                    {
                        asset = key,
                        name,
                        x = R(p.x), y = R(p.y), z = R(p.z),
                        described = EnvDescriptions.HasAssetText(key),
                    };
            }
            var list = new List<object>();
            foreach (var kv in seen)
                list.Add(new { entry = kv.Value, count = counts[kv.Key] });
            return JsonConvert.SerializeObject(new { assets = list });
        }

        /// <summary>Which room a world point resolves to (-1 = none) — the validate mode's anchor check.</summary>
        public static string RoomIdAt(float x, float y, float z)
        {
            var room = RoomMap.RoomAt(new Vector3(x, y, z));
            return JsonConvert.SerializeObject(new { id = room != null ? room.Id : -1 });
        }

        // ---- camera / fog (stateful) ----

        private static bool _saved;
        private static Vector3 _savedPos;
        private static float _savedYaw, _savedZoom;
        private static readonly List<KeyValuePair<FogOfWarArea, bool>> _savedFog
            = new List<KeyValuePair<FogOfWarArea, bool>>();

        /// <summary>Aim the camera for a capture: fog cheat-revealed, immediate scroll to the point,
        /// held yaw (135 = canonical: north upper-right), normalized zoom (0 = in, 1 = out; the zoom
        /// smooths over a few frames — the driver waits before /screenshot). Saves the previous camera +
        /// fog state on FIRST use so <see cref="Restore"/> can undo the whole session.</summary>
        public static string Frame(float x, float y, float z, float yaw = 135f, float zoom = 0.5f)
        {
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            if (rig == null) return Err("no camera rig");
            if (!_saved)
            {
                _saved = true;
                _savedPos = rig.transform.position;
                _savedYaw = rig.transform.rotation.eulerAngles.y;
                _savedZoom = rig.CameraZoom != null ? rig.CameraZoom.CurrentNormalizePosition : 0f;
                _savedFog.Clear();
                foreach (var f in FogOfWarArea.All)
                    if (f != null) _savedFog.Add(new KeyValuePair<FogOfWarArea, bool>(f, f.IsCheatOffFog));
            }
            foreach (var f in FogOfWarArea.All) if (f != null) f.IsCheatOffFog = true;
            rig.SetRotation(yaw);
            if (rig.CameraZoom != null) rig.CameraZoom.CurrentNormalizePosition = zoom;
            rig.ScrollToImmediately(new Vector3(x, y, z));
            return JsonConvert.SerializeObject(new { ok = true });
        }

        /// <summary>Screen-space labels for the CURRENT frame: every scan item within radius of the
        /// camera target that projects on screen, as image-style coordinates (origin TOP-left — Unity's
        /// bottom-left y is flipped here) — so the authoring pass can put a name on everything visible
        /// without hide-renderer tricks. Units flagged so prose can exclude them at a glance.</summary>
        public static string Labels(float radius = 30f)
        {
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            var cam = rig != null ? rig.Camera : Camera.main;
            if (cam == null) return Err("no camera");
            var center = rig != null ? rig.transform.position : Vector3.zero;
            var labels = new List<object>();
            foreach (var it in WorldModel.Items)
            {
                Vector3 p;
                string name;
                string asset;
                bool unit;
                try { p = it.Position; name = it.Name; asset = it.AssetKey; unit = it.IsUnit; }
                catch { continue; }
                float dx = p.x - center.x, dz = p.z - center.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > radius) continue;
                var sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;
                labels.Add(new
                {
                    name,
                    asset,
                    unit,
                    sx = (int)sp.x,
                    sy = Screen.height - (int)sp.y, // image coords: origin top-left
                    d = R(d),
                });
            }
            return JsonConvert.SerializeObject(new { w = Screen.width, h = Screen.height, labels });
        }

        /// <summary>Undo the survey session: restore each fog area's cheat flag and the camera's
        /// position/yaw/zoom captured at the first <see cref="Frame"/>.</summary>
        public static string Restore()
        {
            if (!_saved) return JsonConvert.SerializeObject(new { ok = true, note = "nothing to restore" });
            foreach (var kv in _savedFog)
                if (kv.Key != null) kv.Key.IsCheatOffFog = kv.Value;
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            if (rig != null)
            {
                rig.SetRotation(_savedYaw);
                if (rig.CameraZoom != null) rig.CameraZoom.CurrentNormalizePosition = _savedZoom;
                rig.ScrollToImmediately(_savedPos);
            }
            _saved = false;
            _savedFog.Clear();
            return JsonConvert.SerializeObject(new { ok = true });
        }

        // ---- helpers ----

        private static float R(float v) => (float)Math.Round(v, 2);
        private static string Err(string msg) => JsonConvert.SerializeObject(new { error = msg });
    }
}
#endif
