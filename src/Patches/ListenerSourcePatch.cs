using HarmonyLib;
using Kingmaker.Sound;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Make ears-at-cursor deterministic by replacing the game's camera snap AT THE SOURCE.
    /// <c>AudioListenerPositionController.LateUpdate</c> copies the camera pose onto the single
    /// Wwise listener; our <see cref="Exploration.ListenerAnchor"/> re-snapped it afterwards at
    /// +10000 — but <c>AkAudioService</c>'s own LateUpdate consumes the transform (AudioZone
    /// membership + the Wwise listener position push) in the SAME phase, so which value it saw was
    /// undefined script ordering. It happened to land our way; this patch removes the coin flip:
    /// while our override is active the controller writes OUR pose (skip original), so every
    /// consumer sees the cursor ears no matter when it runs. When the override is inactive the
    /// original camera snap runs untouched — vanilla audio with zero cleanup.
    /// </summary>
    [HarmonyPatch(typeof(AudioListenerPositionController), "LateUpdate")]
    internal static class ListenerSourcePatch
    {
        private static bool Prefix(AudioListenerPositionController __instance)
        {
            if (!Exploration.ListenerAnchor.TryGetOverride(out var pos, out var rot)) return true;
            var listener = Owlcat.Runtime.Core.Registry.ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener == null) return true;
            listener.transform.SetPositionAndRotation(pos, rot);
            return false; // camera snap skipped — our pose is the frame's single source of truth
        }
    }
}
