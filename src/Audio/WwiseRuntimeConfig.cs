using System.Reflection;
using HarmonyLib; // AccessTools (protected settings accessor)
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Applies the mod's Wwise-level audio options each frame (polled, like all live settings):
    ///
    /// - <c>audio.panning</c>: Wwise's global stereo panning rule. The game never calls
    ///   SetPanningRule, so it runs on the conservative "Speakers" default; "Headphones" is
    ///   constant-power panning across the full stereo width — audibly wider positioning
    ///   (user A/B on live cues, 2026-07-28). Game audio only; the classic NAudio path has its
    ///   own spatializer.
    /// - <c>audio.background_audio</c>: keep the game audible when the window loses focus.
    ///   Two vanilla mechanisms silence it: the Unity player pauses outright (runInBackground
    ///   is false and nothing in the game writes it), and the Wwise integration suspends the
    ///   sound engine on focus loss unless its RenderDuringFocusLoss advanced setting is on
    ///   (AkSoundEngineController.OnApplicationFocus → Suspend). Setting both keeps the world
    ///   running and audible while alt-tabbed — the screen-reader multitasking case.
    /// </summary>
    internal static class WwiseRuntimeConfig
    {
        private static string _appliedPanning;
        private static bool? _appliedBackground;

        // AkCommonPlatformSettings.GetAdvancedSettings is protected; the object it returns carries
        // the public m_RenderDuringFocusLoss field the focus handler reads live on every focus loss.
        private static readonly MethodInfo GetAdvancedSettings =
            AccessTools.Method(typeof(AkCommonPlatformSettings), "GetAdvancedSettings");

        // The third mute path: SoundState.OnApplicationFocusChanged posts the bank-authored
        // "PauseAudio" event on every focus loss — a global mute that survives the engine-level
        // fixes. Its own kill switch (ApplicationFocusEvents.SoundDisabled, normally only the
        // -focus-disable-sound command line) has a private setter.
        private static readonly MethodInfo SetSoundDisabled =
            AccessTools.PropertySetter(typeof(Kingmaker.Utility.ApplicationFocusEvents), "SoundDisabled");

        public static void Tick()
        {
            TickPanning();
            TickBackgroundAudio();
        }

        private static void TickPanning()
        {
            if (!AkSoundEngine.IsInitialized()) return;
            var pan = ModSettings.GetSetting<ChoiceSetting>("audio.panning")?.ValueId ?? "speakers";
            if (pan == _appliedPanning) return;
            _appliedPanning = pan;
            AkSoundEngine.SetPanningRule(pan == "headphones"
                ? AkPanningRule.AkPanningRule_Headphones : AkPanningRule.AkPanningRule_Speakers);
        }

        private static void TickBackgroundAudio()
        {
            bool on = ModSettings.GetSetting<BoolSetting>("audio.background_audio")?.Get() ?? false;
            if (_appliedBackground != on)
            {
                _appliedBackground = on;
                // Written once per toggle: turning OFF restores the vanilla pause-on-unfocus (the
                // DEBUG dev server re-asserts true on its own tick and wins there — intended).
                UnityEngine.Application.runInBackground = on;
                if (GetAdvancedSettings != null
                    && AkWwiseInitializationSettings.ActivePlatformSettings is AkCommonPlatformSettings common
                    && GetAdvancedSettings.Invoke(common, null) is AkCommonAdvancedSettings adv)
                    adv.m_RenderDuringFocusLoss = on;
                SetSoundDisabled?.Invoke(null, new object[] { on });
                // A "PauseAudio" posted BEFORE the flag flipped never gets its "ResumeAudio" (the
                // flag suppresses both directions) — e.g. enabling the setting while the mute is
                // latched. Post the resume defensively; it's a no-op when audio is already live.
                if (on && AkSoundEngine.IsInitialized())
                    Kingmaker.Sound.SoundEventsManager.PostEvent("ResumeAudio", null);
            }
            else if (on)
                UnityEngine.Application.runInBackground = true; // cheap re-assert, mirrors DevServer
        }
    }
}
