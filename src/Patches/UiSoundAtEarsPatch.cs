using HarmonyLib;
using Kingmaker.Sound; // DefaultListener
using Kingmaker.UI;    // UISoundController, UISoundType
using Owlcat.Runtime.Core.Registry; // ObjectRegistry
using UnityEngine;

namespace WrathAccess.Patches
{
    /// <summary>
    /// UI sounds (hover, clicks, window opens — everything routed through
    /// <see cref="UISoundController"/>) post their Wwise events on a scene-anchored UI object
    /// (<c>Game.Instance.UI.Common</c>). With the vanilla camera-snapped listener that object is
    /// always nearby; with OUR ears at the cursor (+ listener height) it can be tens of metres away,
    /// so every UI sound paid world distance attenuation it was never designed for — the "hover click
    /// got extremely quiet" regression. Re-post them ON the listener itself: interface feedback
    /// belongs at the ears, at full volume, wherever the ears are.
    /// </summary>
    [HarmonyPatch(typeof(UISoundController), nameof(UISoundController.Play),
        new[] { typeof(UISoundType), typeof(GameObject) })]
    internal static class UiSoundAtEarsPatch
    {
        private static void Prefix(ref GameObject gameObject)
        {
            if (!Main.Enabled) return;
            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener != null) gameObject = listener.gameObject;
        }
    }
}
