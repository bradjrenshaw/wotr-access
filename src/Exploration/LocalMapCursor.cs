using System.IO;
using Kingmaker;
using Kingmaker.Blueprints.Area; // AreaService (LocalMapBounds)
using Kingmaker.Controllers.Clicks.Handlers; // ClickGroundHandler (move party, the map's right-click)
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // LocalMapModel (IsInCurrentArea)
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays; // OverlayAudio, OverlayManager, MovementSlot, GridSystem
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The map screen's MOVEMENT cursor — a free sweep over the area's map rectangle with WASD/arrows,
    /// its own point so a map peek never disturbs the in-area <see cref="Cursor"/> (the analogue of
    /// <see cref="GlobalMapCursor"/>, area space instead of world-map space). Each slot reuses its
    /// IN-AREA movement type and glide speed at <see cref="SpeedFactor"/>x — the map is for covering
    /// ground fast. Movement is unconstrained by the navmesh (a map lets you fly over walls) but clamped
    /// to the area part's LocalMapBounds; the height re-projects onto the walkable surface where there
    /// is one. Speech-only feedback (v1): room changes narrate while moving, landing on a marker speaks
    /// it (with the shared object enter/leave cue), and settling on one after a glide reads it once.
    /// <b>Enter</b> plants the in-area cursor at the map cursor (peek → commit); <b>Backspace</b> sends
    /// the party (the vanilla map's right-click); <b>C</b> recenters on the leader; <b>K</b> reads the
    /// spot; <b>/</b>/<b>Home</b> jumps to the review selection; <b>Z</b>/<b>X</b> = where-am-I /
    /// room description at the map cursor.
    /// </summary>
    internal static class LocalMapCursor
    {
        private const float SpeedFactor = 3f;   // map glide = the slot's in-area speed x this
        private const float LandRadius = 1.0f;  // minimum "on it" reach around a marker's footprint

        private static Vector3? _pos;
        private static ScanItem _inside;     // the marker the cursor is on (for the enter/leave cue)
        private static ScanItem _spoken;     // last marker the idle-settle readout spoke (null = armed)
        private static RoomMap.Room _room;   // the room under the cursor (narrated on change while moving)
        private static bool _baselined;      // don't fire the cue/narration on the first tick after open
        private static bool _onNavmesh = true; // whether the cursor sits on walkable ground (set by SetPos)
        private static bool _wasMoving;      // last frame's movement state (for the settle readouts)

        // Per-slot typematic state for tiled stepping (one step on press, a pause, then repeats while held).
        private sealed class TiledState { public bool Holding; public float NextStep; }
        private static readonly TiledState _primaryTiled = new TiledState();
        private static readonly TiledState _secondaryTiled = new TiledState();

        /// <summary>On map open: plant at the in-area cursor (the spot you were exploring), else the leader.</summary>
        public static void Reset()
        {
            _pos = Cursor.Has ? Cursor.Position : (Vector3?)LeaderPos();
            _inside = null; _spoken = null; _baselined = false;
            _room = null;
            _primaryTiled.Holding = false; _secondaryTiled.Holding = false;
        }

        public static Vector3 Position => _pos ?? LeaderPos();

        private static Vector3 LeaderPos()
            => Game.Instance?.Player?.MainCharacter.Value?.Position ?? Vector3.zero;

        // Per-frame movement + the cue/narration. InputManager.Held respects the claim chain, so the
        // localmap.cursor* keys only read held while the map screen's category owns them.
        public static void Tick(float dt)
        {
            if (WrathAccess.Screens.ScreenManager.Current?.Key != WrathAccess.Screens.LocalMapScreen.ScreenKey)
            {
                _baselined = false;
                _primaryTiled.Holding = false; _secondaryTiled.Holding = false;
                return;
            }

            bool continuous = false, tiled = false;
            MoveSlot("localmap.cursor", MovementSlot.Primary, dt, _primaryTiled, ref continuous, ref tiled);
            MoveSlot("localmap.secondary", MovementSlot.Secondary, dt, _secondaryTiled, ref continuous, ref tiled);

            var inside = MarkerAt(Position);

            if (!_baselined)
            {
                _inside = inside; _spoken = inside; _room = RoomMap.RoomAt(Position);
                _baselined = true;
                return;
            }

            // Room narration — the primary by-ear feedback while sweeping. Fires on entering a
            // DIFFERENT room the player has actually seen some of; never-visited rooms stay anonymous
            // (no id/class leak from the map), and corridor/unmapped space stays quiet (K/Z on demand).
            bool moving = continuous || tiled;
            if (moving)
            {
                var room = RoomMap.RoomAt(Position);
                if (room != null && room != _room && Known(room)) Tts.Speak(RoomMap.Describe(room), interrupt: true);
                _room = room;
            }

            // Marker cue: the shared object enter/leave wavs, on a change of the marker we're on.
            if (inside != _inside) { PlayCue(inside != null); _inside = inside; }

            // Continuous idle-settle: gliding is too fast to narrate every marker, so once the keys are
            // released, speak the one the cursor sits on, once. Tiled announces each landing itself.
            if (continuous || inside == null) _spoken = null;
            else if (!tiled && inside != _spoken) { Tts.Speak(inside.DescribeInPlace()); _spoken = inside; }

            // Coming to rest over unwalkable ground says so — the by-ear equivalent of the map's
            // grayed-out void (the in-area cursor can never get here, only the map's free sweep can).
            if (_wasMoving && !moving && !_onNavmesh && inside == null)
                Tts.Speak(Loc.T("localmap.not_walkable"));
            _wasMoving = moving;
        }

        /// <summary>A room the player has seen NONE of stays anonymous on the map.</summary>
        private static bool Known(RoomMap.Room room)
            => room != null && RoomMap.UnexploredPercent(room) < 100;

        // One slot's movement this frame, on its IN-AREA movement type (continuous / tiled / none).
        private static void MoveSlot(string prefix, MovementSlot slot, float dt, TiledState st,
            ref bool continuous, ref bool tiled)
        {
            int dx = 0, dz = 0;
            if (InputManager.Held(prefix + "Up")) dz += 1;
            if (InputManager.Held(prefix + "Down")) dz -= 1;
            if (InputManager.Held(prefix + "Right")) dx += 1;
            if (InputManager.Held(prefix + "Left")) dx -= 1;

            string mode = ModeOf(slot);
            if (mode == "none" || (dx == 0 && dz == 0)) { st.Holding = false; return; }

            if (mode == "tiled") { tiled = true; TiledStep(dx, dz, st); return; }

            continuous = true;
            SetPos(Position + new Vector3(dx, 0f, dz).normalized * (Speed(slot) * dt));
        }

        // The slot's in-area settings on the engaged overlay — the map reuses the user's normal movement
        // type and speed (x SpeedFactor), so there's no second set of movement knobs to configure.
        private static CategorySetting SlotCat(MovementSlot slot)
            => OverlayManager.ActiveOverlay?.Cursor?.Slot(slot);

        private static string ModeOf(MovementSlot slot)
            => SlotCat(slot)?.Get<ChoiceSetting>("mode")?.Current?.Id ?? "continuous";

        private static float Speed(MovementSlot slot)
            => (SlotCat(slot)?.Get<IntSetting>("speed")?.Get() ?? 15) * Geo.MetresPerFoot * SpeedFactor;

        // Clamp into the map rectangle, then re-seat the height on the walkable surface where one exists
        // (so Enter/where-am-I read the right floor); off-navmesh spots keep their last height.
        private static void SetPos(Vector3 p)
        {
            var part = AreaService.Instance?.CurrentAreaPart;
            var pb = part != null ? part.Bounds : null;
            if (pb != null)
            {
                var b = pb.LocalMapBounds;
                p.x = Mathf.Clamp(p.x, b.min.x, b.max.x);
                p.z = Mathf.Clamp(p.z, b.min.z, b.max.z);
            }
            var s = NavmeshProbe.Sample(p.x, p.z, p.y);
            if (s.OnNavmesh) p.y = s.Point.y;
            _onNavmesh = s.OnNavmesh;
            _pos = p;
        }

        // Typematic tiled stepping on the in-area grid cell (mirrors TileStep's cadence; diagonals
        // stretch the interval by sqrt(2) so held-diagonal ground speed matches cardinal).
        private static void TiledStep(int dx, int dz, TiledState st)
        {
            float stretch = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
            float now = Time.unscaledTime;
            if (!st.Holding) { st.Holding = true; st.NextStep = now + OsKeyboard.InitialDelay; DoTiledStep(dx, dz); }
            else if (now >= st.NextStep) { st.NextStep = now + OsKeyboard.RepeatInterval * stretch; DoTiledStep(dx, dz); }
        }

        private static void DoTiledStep(int dx, int dz)
        {
            float cell = OverlayManager.ActiveOverlay?.Get<GridSystem>()?.CellSize ?? (5f * Geo.MetresPerFoot);
            var p = Position;
            SetPos(new Vector3(Snap(p.x, cell) + dx * cell, p.y, Snap(p.z, cell) + dz * cell));

            var on = MarkerAt(Position);
            if (on != null) { Tts.Speak(on.DescribeInPlace(), interrupt: true); _spoken = on; }
            else if (!_onNavmesh)
                Tts.Speak(Loc.T("localmap.not_walkable") + ", " + PositionLine(), interrupt: true);
            else Tts.Speak(PositionLine(), interrupt: true);
        }

        private static float Snap(float v, float cell) => (Mathf.Floor(v / cell) + 0.5f) * cell;

        private static void PlayCue(bool enter)
        {
            float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
            AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, enter ? "object_enter.wav" : "object_exit.wav"), vol);
        }

        // The nearest cached marker whose footprint (plus a comfortable landing pad) contains the cursor.
        private static ScanItem MarkerAt(Vector3 p)
        {
            ScanItem best = null;
            float bd = float.MaxValue;
            var list = LocalMapReview.CachedMarkers;
            for (int i = 0; i < list.Count; i++)
            {
                float d = list[i].DistanceTo(p);
                if (d <= LandRadius && d < bd) { bd = d; best = list[i]; }
            }
            return best;
        }

        // Bearing + distance from the party leader — the anchor everything on a map is relative to.
        private static string PositionLine()
            => Loc.T("localmap.position", new
            {
                bearing = Geo.Bearing(LeaderPos(), Position),
                feet = Geo.FeetStr(Geo.Distance(LeaderPos(), Position)),
            });

        // ---- on-demand keys ----

        /// <summary>C: snap to the party leader.</summary>
        public static void Recenter() { SetPos(LeaderPos()); Settle(); }

        /// <summary>/ and Home: snap to the review selection.</summary>
        public static void JumpToReview()
        {
            var sel = LocalMapReview.Selected;
            if (sel == null) { Tts.Speak(Loc.T("localmap.no_selection"), interrupt: true); return; }
            SetPos(sel.Position);
            Settle();
        }

        // Snap-and-read: speak what we landed on and baseline the cue + idle readout.
        private static void Settle()
        {
            var on = MarkerAt(Position);
            _inside = on; _spoken = on; _room = RoomMap.RoomAt(Position);
            if (on != null) Tts.Speak(on.DescribeInPlace(), interrupt: true);
            else if (!_onNavmesh) Tts.Speak(Loc.T("localmap.not_walkable") + ", " + PositionLine(), interrupt: true);
            else if (Known(_room)) Tts.Speak(RoomMap.Describe(_room), interrupt: true);
            else Tts.Speak(PositionLine(), interrupt: true);
        }

        /// <summary>K: read the spot — the marker under the cursor, else walkability + bearing/distance.</summary>
        public static void Announce()
        {
            var on = MarkerAt(Position);
            if (on != null) Tts.Speak(on.DescribeInPlace(), interrupt: true);
            else if (!_onNavmesh) Tts.Speak(Loc.T("localmap.not_walkable") + ", " + PositionLine(), interrupt: true);
            else Tts.Speak(PositionLine(), interrupt: true);
        }

        /// <summary>Z: the in-area where-am-I recipe, evaluated at the map cursor.</summary>
        public static void WhereAmI()
        {
            var parts = new System.Collections.Generic.List<string>();
            var area = Game.Instance?.CurrentlyLoadedArea;
            if (area != null) parts.Add(TextUtil.StripRichText(area.AreaDisplayName));
            var part = AreaService.Instance?.CurrentAreaPart;
            if (part != null && part.IsIndoor) parts.Add(Loc.T("where.indoors"));
            var room = RoomMap.RoomAt(Position);
            if (Known(room)) parts.Add(RoomMap.Describe(room)); // never-seen rooms stay anonymous
            if (!FogExplored.IsExplored(Position)) parts.Add(Loc.T("where.unexplored"));
            var pb = part != null ? part.Bounds : null;
            if (pb != null)
            {
                var b = pb.LocalMapBounds;
                float fx = Mathf.Clamp01((Position.x - b.min.x) / Mathf.Max(b.size.x, 0.001f));
                float fz = Mathf.Clamp01((Position.z - b.min.z) / Mathf.Max(b.size.z, 0.001f));
                parts.Add(Loc.T("where.region", new { region = Scanner.RegionWord(fx, fz) }));
            }
            parts.Add(PositionLine());
            Tts.Speak(string.Join(", ", parts), interrupt: true);
        }

        /// <summary>X: the mod-authored room description at the map cursor — but a room the player has
        /// never seen keeps its secrets (the map must not read ahead of exploration).</summary>
        public static void DescribeRoom()
        {
            if (!Known(RoomMap.RoomAt(Position))) { Tts.Speak(Loc.T("where.unexplored"), interrupt: true); return; }
            var msg = EnvDescriptions.DescribeRoom(Position);
            if (!string.IsNullOrEmpty(msg)) Tts.Speak(msg, interrupt: true);
        }

        /// <summary>Enter: plant the IN-AREA cursor here (peek → commit) — the walkable surface only,
        /// so the world cursor never lands somewhere the sonification can't stand.</summary>
        public static void SyncWorldCursor()
        {
            var p = Position;
            var s = NavmeshProbe.Sample(p.x, p.z, p.y);
            if (!s.OnNavmesh) { Tts.Speak(Loc.T("localmap.not_walkable"), interrupt: true); return; }
            Cursor.Set(s.Point);
            Tts.Speak(Loc.T("localmap.cursor_set"), interrupt: true);
        }

        /// <summary>Backspace: send the party here — exactly the vanilla map's right-click
        /// (clamp into the area, then the game's own selection-aware move).</summary>
        public static void MoveParty()
        {
            var p = Position;
            if (!LocalMapModel.IsInCurrentArea(p))
            {
                var part = AreaService.Instance?.CurrentAreaPart;
                var pb = part != null ? part.Bounds : null;
                if (pb != null) p = pb.LocalMapBounds.ClosestPoint(p);
            }
            ClickGroundHandler.MoveSelectedUnitsToPoint(p);
            Tts.Speak(Loc.T("localmap.move_party"), interrupt: true);
        }
    }
}
