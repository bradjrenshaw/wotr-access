using HarmonyLib;
using Kingmaker.UI.MVVM._PCView.LoadingScreen;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Speaks the LOADING screen's tip — the "the game is loading an area now" screen, not the
    /// load-a-game window. The view rolls its hint inside its private <c>Show()</c>
    /// (<c>m_Hint.text = m_Hints.TakeHint()</c>), so postfix it and read the label it just set:
    /// the game's own localized tip, passed through verbatim.
    /// </summary>
    [HarmonyPatch(typeof(LoadingScreenPCView), "Show")]
    internal static class LoadingTipPatch
    {
        private static void Postfix(LoadingScreenPCView __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                var tip = __instance.m_Hint != null ? __instance.m_Hint.text : null;
                if (string.IsNullOrWhiteSpace(tip)) return;
                Tts.Speak(Loc.T("loading.tip", new { tip = TextUtil.StripRichText(tip) }), interrupt: true);
            }
            catch { } // speech is non-essential; never break the load
        }
    }
}
