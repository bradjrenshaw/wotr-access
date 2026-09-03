using Kingmaker;
using Kingmaker.GameModes;
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Low-vision camera mode (user-designed): glue the game camera to OUR cursor at a persistent,
    /// user-tuned offset and relative angle, so the view moves the way the player expects instead of
    /// the mod stealing the camera (the old behaviour engaged the game's follower on the lead
    /// character on every move order). Settings live under Vision/Camera: the follow toggle, a
    /// sideways/forward offset in feet (VIEW-frame: "forward" is up the screen), and a camera angle
    /// relative to the LISTENER's facing — turn the cursor with Q/E and the camera swings with it,
    /// keeping the arrangement. While the mode is on, the existing camera keys change meaning:
    /// Alt+Q/E adjust the relative angle (person-frame, same convention as the listener), Alt+WASD
    /// nudge the stored offsets — every adjustment persists as the setting. Zoom keys work in both
    /// modes (they drive the game's own zoom).
    ///
    /// The glue runs in LateUpdate at +10000 (after the game's camera logic, beside
    /// <see cref="ListenerAnchor"/>) and releases the game's follower each frame so nothing fights
    /// it. Cutscenes, dialogue, rest and the world map are left to the game — same gates as the
    /// listener override, and turning the setting off restores vanilla with zero cleanup.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class CameraFollowCursor : MonoBehaviour
    {
        private const int AngleStepDeg = 15;
        private const int OffsetStepFt = 5;

        public static void RegisterSettings()
        {
            ModSettingsRegistry.EnsureCategory("vision", "Vision", "category.vision");
            var cam = ModSettingsRegistry.EnsureCategory("vision.camera", "Vision/Camera", "vision.camera");
            if (cam.GetByKey("follow_cursor") == null)
                cam.Add(new BoolSetting("follow_cursor", "Camera follows the cursor", false, "vision.camera.follow_cursor"));
            // The range is OUR slider bound only — the game has no distance cap; its real limit is the
            // area's camera-bounds polygon, which the glue applies every frame (ClampByLevelBounds
            // below), exactly the edge sighted players hit when panning. ±1000 ft outranges any area.
            if (cam.GetByKey("offset_x") == null)
                cam.Add(new IntSetting("offset_x", "Sideways offset (feet)", 0, -1000, 1000, OffsetStepFt, "vision.camera.offset_x"));
            if (cam.GetByKey("offset_z") == null)
                cam.Add(new IntSetting("offset_z", "Forward offset (feet)", 0, -1000, 1000, OffsetStepFt, "vision.camera.offset_z"));
            if (cam.GetByKey("angle") == null)
                cam.Add(new IntSetting("angle", "Angle relative to facing (degrees)", 0, -180, 180, AngleStepDeg, "vision.camera.angle"));
            // Preferred zoom, enforced by the glue: the game's zoom resets on every area load, which
            // throws away a low-vision player's magnification. Alt+PageUp/Down adjust this while the
            // mode is on (persisted after the presses stop); the vanilla transient zoom stands when off.
            if (cam.GetByKey("zoom") == null)
                cam.Add(new IntSetting("zoom", "Preferred zoom (percent)", 50, 0, 100, 5, "vision.camera.zoom"));
            // Camera glide: 0 = instant (the old behaviour). A cursor jump across the map otherwise
            // teleports the view — disorienting/motion-sickening. Exponential smoothing: small steps
            // stay crisp, big jumps become a quick swoop.
            if (cam.GetByKey("smoothing") == null)
                cam.Add(new IntSetting("smoothing", "Smoothing (0 = instant)", 30, 0, 100, 5, "vision.camera.smoothing"));
        }

        private static IntSetting OffsetX => ModSettings.GetSetting<IntSetting>("vision.camera.offset_x");
        private static IntSetting OffsetZ => ModSettings.GetSetting<IntSetting>("vision.camera.offset_z");
        private static IntSetting Angle => ModSettings.GetSetting<IntSetting>("vision.camera.angle");
        private static IntSetting Zoom => ModSettings.GetSetting<IntSetting>("vision.camera.zoom");
        private static IntSetting Smoothing => ModSettings.GetSetting<IntSetting>("vision.camera.smoothing");

        /// <summary>Set while something else legitimately holds the camera (the dev survey's Frame()
        /// captures — it scrolls to a room and this component was dragging the rig straight back to
        /// the cursor, so every capture showed the party; 2026-09-01).</summary>
        public static bool Suspended;

        /// <summary>The mode is switched on AND the game state lets us own the camera this frame.</summary>
        public static bool Active
        {
            get
            {
                if (!Main.Enabled || Suspended) return false;
                if (!(ModSettings.GetSetting<BoolSetting>("vision.camera.follow_cursor")?.Get() ?? false)) return false;
                var game = Game.Instance;
                if (game == null || game.CurrentlyLoadedArea == null) return false;
                if (game.CutsceneLock.Active) return false;
                var mode = game.CurrentMode;
                if (mode == GameModeType.Cutscene || mode == GameModeType.Dialog
                    || mode == GameModeType.Rest || mode == GameModeType.GlobalMap) return false;
                return true;
            }
        }

        // ---- continuous key adjustments (held-key polled, like the listener's Q/E turn) ----

        // While held, adjustments accumulate in FLOAT shadows and only write back to the (int)
        // settings on release — one persistence write per gesture, not per frame. Speeds: pan
        // matches the game's own keyboard scroll (CameraScrollSpeedKeyboard * 0.02 per frame, the
        // exact formula CameraRig.AddUp uses); rotation matches the listener's continuous turn.
        private const float TurnSpeedDegPerSec = 90f; // same as ListenerFrame.TurnSpeed
        private bool _adjusting;
        private float _angleLive, _oxLive, _ozLive; // degrees / feet, valid while _adjusting

        private void PollAdjustKeys()
        {
            if (ClearGesture) { ClearGesture = false; _adjusting = false; } // Reset dropped the gesture
            int rot = 0, dx = 0, dz = 0;
            if (Input.InputManager.Held("camera.rotateLeft")) rot -= 1;
            if (Input.InputManager.Held("camera.rotateRight")) rot += 1;
            if (Input.InputManager.Held("camera.panUp")) dz += 1;
            if (Input.InputManager.Held("camera.panDown")) dz -= 1;
            if (Input.InputManager.Held("camera.panLeft")) dx -= 1;
            if (Input.InputManager.Held("camera.panRight")) dx += 1;

            if (rot == 0 && dx == 0 && dz == 0)
            {
                if (!_adjusting) return;
                // Gesture ended: persist once and speak the final arrangement.
                _adjusting = false;
                int a = Mathf.RoundToInt(_angleLive);
                if (a > 180) a -= 360; if (a < -180) a += 360;
                Angle?.Set(a);
                OffsetX?.Set(Mathf.RoundToInt(_oxLive));
                OffsetZ?.Set(Mathf.RoundToInt(_ozLive));
                if (_gestureTurned)
                    Tts.Speak(Loc.T("camera.angle_set", new { angle = Angle?.Get() ?? a }), interrupt: true);
                if (_gesturePanned)
                    Tts.Speak(Loc.T("camera.offset_set", new { x = OffsetX?.Get() ?? 0, z = OffsetZ?.Get() ?? 0 }),
                        interrupt: !_gestureTurned);
                return;
            }

            if (!_adjusting)
            {
                _adjusting = true;
                _angleLive = Angle?.Get() ?? 0;
                _oxLive = OffsetX?.Get() ?? 0;
                _ozLive = OffsetZ?.Get() ?? 0;
                _gestureTurned = _gesturePanned = false;
            }
            if (rot != 0)
            {
                _angleLive += rot * TurnSpeedDegPerSec * Time.unscaledDeltaTime;
                if (_angleLive > 180f) _angleLive -= 360f;
                if (_angleLive < -180f) _angleLive += 360f;
                _gestureTurned = true;
            }
            if (dx != 0 || dz != 0)
            {
                // The game's per-frame keyboard scroll step, converted to feet (our storage unit).
                float stepFt = (float)Kingmaker.Settings.SettingsRoot.Controls.CameraScrollSpeedKeyboard
                    * 0.02f / Geo.MetresPerFoot;
                _oxLive = Mathf.Clamp(_oxLive + dx * stepFt, OffsetX?.Min ?? -1000, OffsetX?.Max ?? 1000);
                _ozLive = Mathf.Clamp(_ozLive + dz * stepFt, OffsetZ?.Min ?? -1000, OffsetZ?.Max ?? 1000);
                _gesturePanned = true;
            }
        }
        private bool _gestureTurned, _gesturePanned; // what the gesture touched → what to announce on release

        /// <summary>Alt+R: reset the arrangement — offsets to zero, relative angle to zero, so the
        /// camera sits on the cursor facing wherever the cursor faces (north unless turned).</summary>
        public static void Reset()
        {
            if (!Active) return;
            Angle?.Set(0);
            OffsetX?.Set(0);
            OffsetZ?.Set(0);
            ClearGesture = true; // a held adjust gesture must not write back over the reset on release
            Tts.Speak(Loc.T("camera.reset"), interrupt: true);
        }

        /// <summary>Abandon any in-flight held-key gesture (used by Reset via the instance).</summary>
        internal static bool ClearGesture; // set true to drop the live floats next frame

        /// <summary>Alt+PageUp/Down while the mode is on: adjust the preferred zoom (announced per
        /// press, persisted once the presses stop — repeating keys would otherwise write the settings
        /// file at repeat rate).</summary>
        public static void NudgeZoom(int dir)
        {
            var s = Zoom;
            if (s == null) return;
            _zoomPendingPct = Mathf.Clamp((_zoomPendingPct < 0 ? s.Get() : _zoomPendingPct) + dir * s.Step, 0, 100);
            _zoomPersistAt = Time.unscaledTime + 0.6f;
            Tts.Speak(Loc.T("camera.zoom", new { percent = _zoomPendingPct }), interrupt: true);
        }
        private static int _zoomPendingPct = -1;   // -1 = no pending adjustment; else the live target %
        private static float _zoomPersistAt;       // when to write the pending value into the setting

        /// <summary>The zoom percent the glue enforces this frame (pending adjustment wins).</summary>
        private static int TargetZoomPct => _zoomPendingPct >= 0 ? _zoomPendingPct : (Zoom?.Get() ?? 50);

        // ---- the per-frame glue ----

        private void LateUpdate()
        {
            if (!Active) { _adjusting = false; _hadFrame = false; return; }
            var rig = Game.Instance?.UI?.GetCameraRig();
            if (rig == null) return;

            PollAdjustKeys();
            PersistPendingZoom();

            // Anchor: the cursor when placed, else the player reference (TB-aware) — same rule as the ears.
            var anchor = Cursor.Has ? Cursor.Position.Value : Overlays.Cursor.PlayerPosition;

            float yaw = ListenerFrame.Facing + (_adjusting ? _angleLive : (Angle?.Get() ?? 0));
            // Offsets are stored in the VIEW frame, so the arrangement turns with the camera: rotate
            // them by the camera yaw into world space. Feet → metres like every spoken distance.
            float ox = (_adjusting ? _oxLive : (OffsetX?.Get() ?? 0)) * Geo.MetresPerFoot;
            float oz = (_adjusting ? _ozLive : (OffsetZ?.Get() ?? 0)) * Geo.MetresPerFoot;
            // The area's camera-bounds clamp: our scroll path bypasses the clamp normal scrolling
            // gets, so apply the rig's own — a big offset stops at the map edge like a sighted pan does.
            var target = rig.ClampByLevelBounds(anchor + Quaternion.Euler(0f, yaw, 0f) * new Vector3(ox, 0f, oz));

            // GLIDE, don't teleport: exponential smoothing toward the target — a cursor step tracks
            // near-instantly, a jump across the map becomes a fast swoop instead of a hard cut
            // (motion-sickness guard, user-raised). 0 disables. The half-life scales with the setting
            // (100 → ~0.5 s); frame-rate independent via the exp form.
            int smooth = Smoothing?.Get() ?? 0;
            float k = 1f; // fraction of the remaining distance covered this frame
            if (smooth > 0 && _hadFrame)
            {
                float halfLife = smooth * 0.005f;
                k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 0.6931f / halfLife);
            }
            _hadFrame = true;

            float curYaw = rig.transform.eulerAngles.y;
            float nextYaw = k >= 1f ? yaw : Mathf.LerpAngle(curYaw, yaw, k);
            // Current position from the rig itself (ground-snapped last frame) — self-correcting, no drift.
            var next = k >= 1f ? target : Vector3.Lerp(rig.transform.position, target, k);

            // The game's follower would fight the glue (it re-scrolls to its unit every frame) — release it.
            Game.Instance.CameraController?.Follower?.Release();
            rig.SetRotation(nextYaw);
            rig.ScrollToImmediately(next);

            // Enforce the preferred zoom (smoothed by the same factor so key presses ease rather than
            // step). ZoomToImmediate writes all three zoom positions — the only set that sticks.
            var zoom = rig.CameraZoom;
            if (zoom != null && ZoomLenF != null)
            {
                float len = (float)ZoomLenF.GetValue(zoom);
                float cur = zoom.CurrentNormalizePosition;
                float want = TargetZoomPct / 100f;
                float nextZoom = k >= 1f ? want : Mathf.Lerp(cur, want, k);
                if (Mathf.Abs(nextZoom - cur) > 0.0005f || k >= 1f)
                    zoom.ZoomToImmediate(nextZoom * len);
            }
        }
        private bool _hadFrame; // first active frame snaps (no stale rig pose to glide from)

        private static readonly System.Reflection.FieldInfo ZoomLenF =
            typeof(Kingmaker.View.CameraZoom).GetField("m_ZoomLenght",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static void PersistPendingZoom()
        {
            if (_zoomPendingPct < 0 || Time.unscaledTime < _zoomPersistAt) return;
            Zoom?.Set(_zoomPendingPct);
            _zoomPendingPct = -1;
        }
    }
}
