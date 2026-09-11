using HarmonyLib;
using Kingmaker;
using Kingmaker.Achievements;
using Kingmaker.Settings;

namespace WrathAccess.Patches
{
    /// <summary>
    /// ENHANCEMENT: achievements stay enabled with mods active. The game's achievement gate
    /// (<c>AchievementEntity.IsDisabled</c>) refuses every unlock when <c>Player.ModsUser</c> is set
    /// or <c>OwlcatModificationsManager.IsAnyModActive</c> reads true — and a native mod like this
    /// one always trips the latter, which in turn marks the save as a mods-user's for good. An
    /// accessibility mod is not a cheat; the toggle lets a blind player earn the same achievements a
    /// sighted one does.
    ///
    /// Patched at the GATE, not at the two flag getters: those are trivial auto-properties that the
    /// Mono JIT inlines into the gate's compiled body, so postfixes on them read false from our
    /// own calls yet change nothing inside IsDisabled (verified live: both getters patched and
    /// false, all 154 achievements still disabled). The gate itself is a real method. When it says
    /// disabled and the toggle is on, re-run its OTHER clauses (platform, campaign, difficulty,
    /// crusade difficulty, ironman — the game is patch-frozen, so this mirror can't drift) and
    /// answer with those alone. Read live per call, so the Enhancements toggle takes effect at once.
    /// </summary>
    [HarmonyPatch(typeof(AchievementEntity), nameof(AchievementEntity.IsDisabled), MethodType.Getter)]
    internal static class AchievementsGatePatch
    {
        private static void Postfix(AchievementEntity __instance, ref bool __result)
        {
            if (!__result || !Enhancements.AchievementsWithMods) return;
            try { __result = DisabledWithoutMods(__instance); }
            catch { }
        }

        // AchievementEntity.IsDisabled minus its final mods clause.
        private static bool DisabledWithoutMods(AchievementEntity a)
        {
            var data = a.Data;
            var player = Game.Instance?.Player;
            if (data == null || player == null) return true;
            if (data.ExcludedFromCurrentPlatform) return true;
            if (data.OnlyMainCampaign && !player.Campaign.IsMainGameContent) return true;
            var specific = data.SpecificCampaign?.Get();
            if (!data.OnlyMainCampaign && specific != null && player.Campaign != specific) return true;
            if (data.MinDifficulty != null
                && player.MinDifficultyController.MinDifficulty.CompareTo(data.MinDifficulty.Preset) < 0) return true;
            if (data.MinCrusadeDifficulty > (KingdomDifficulty)SettingsRoot.Difficulty.KingdomDifficulty) return true;
            if (data.IronMan && !SettingsRoot.Difficulty.OnlyOneSave) return true;
            return false;
        }
    }
}
