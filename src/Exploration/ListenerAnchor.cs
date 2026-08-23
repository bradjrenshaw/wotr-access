using Kingmaker;
using Kingmaker.GameModes;
using Kingmaker.Sound;               // DefaultListener (the game's single Wwise listener)
using Owlcat.Runtime.Core.Registry;  // ObjectRegistry
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The "virtual head" — move the EARS, not the camera (user-designed). The game's 3D audio is
    /// heard from a single Wwise listener (<see cref="DefaultListener"/>), normally snapped to the
    /// camera by <c>AudioListenerPositionController</c> every LateUpdate — which puts footsteps,
    /// combat, barks and ambience in a camera-relative frame that disagrees with our sonification
    /// (sonar/wall tones pan from the cursor, compass-stable). This component runs at +10000, AFTER
    /// the game's controller, and re-snaps the listener onto our reference point (the cursor, else
    /// the leader; or always the leader) at ear height with a fixed NORTH-facing orientation (+Z,
    /// matching Geo's compass) — one spatial frame for everything. While we write, we win; the
    /// moment we stop — cutscenes, dialogs, rest, or the "camera" setting — the game's own per-frame
    /// snap restores vanilla camera audio with zero cleanup. The camera itself is never touched, so
    /// visuals and scripted sequences are unaffected.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class ListenerAnchor : MonoBehaviour
    {
        // Height above the anchor, user-tunable: the game's attenuation curves are calibrated for a
        // listener hanging well above the field (the camera boom), so ear-height made medium-range
        // things sound too close. Straight up keeps the compass symmetric (no north/south bias).
        private static float HeightMetres =>
            (ModSettings.GetSetting<IntSetting>("audio.listener_height")?.Get() ?? 35) * Geo.MetresPerFoot;

        /// <summary>The override pose, when our ears-at-cursor mode should own the listener this
        /// frame; false = vanilla camera audio (setting, cutscene/dialog/rest, no area). Shared with
        /// <see cref="WrathAccess.Patches.ListenerSourcePatch"/>, which replaces the game's camera
        /// snap AT THE SOURCE — zone membership and the Wwise position push both consume whatever
        /// the transform holds when the audio service's LateUpdate reads it, and with three writers
        /// in the same phase the outcome was script-ordering luck (ahicks' camera-culling question,
        /// 2026-08-21: no distance culling queries the camera, but this ordering did).</summary>
        internal static bool TryGetOverride(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            var game = Game.Instance;
            if (game == null || game.CurrentlyLoadedArea == null) return false;

            var choice = ModSettings.GetSetting<ChoiceSetting>("audio.listener")?.ValueId ?? "cursor";
            if (choice == "camera") return false; // vanilla: leave the game's camera snap in charge

            // Cutscenes/dialogs/rest are framed and mixed for the camera, and the player isn't
            // navigating — fall back by simply not overriding.
            if (game.CutsceneLock.Active) return false;
            var mode = game.CurrentMode;
            if (mode == GameModeType.Cutscene || mode == GameModeType.Dialog || mode == GameModeType.Rest) return false;

            var anchor = (choice == "cursor" && Cursor.Has)
                ? Cursor.Position.Value
                : Overlays.Cursor.PlayerPosition; // the TB-aware reference (acting unit in turn-based)
            // The ears face the LISTENER's facing (default north): game audio — voice barks, ambience,
            // combat — pans in the same rotatable frame as the mod's own sounds.
            pos = anchor + Vector3.up * HeightMetres;
            rot = Quaternion.Euler(0f, ListenerFrame.Facing, 0f);
            return true;
        }

        // Belt-and-braces alongside the source patch: also re-snap late in the frame, covering any
        // writer the patch doesn't intercept. Same value both times — idempotent.
        private void LateUpdate()
        {
            if (!TryGetOverride(out var pos, out var rot)) return;
            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener == null) return;
            listener.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
