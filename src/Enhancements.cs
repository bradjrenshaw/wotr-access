namespace WrathAccess
{
    /// <summary>
    /// ENHANCEMENTS: deliberate deviations from sighted parity that materially help blind players
    /// without spoiling the experience — each an opt-outable, documented exception to the
    /// surface-only-what's-visible rule, gathered under one settings category so the boundary
    /// between "the game's truth" and "the mod helping" stays legible.
    /// </summary>
    internal static class Enhancements
    {
        /// <summary>Revealed neutral/bystander NPCs stay locatable while out of sight — on the MAP
        /// (the sighted map hides fogged units entirely) AND in the in-area scanner lists/cycles
        /// (Scanner.RevealedNeutral): re-finding a met vendor by sweeping an area is sighted-cheap
        /// and blind-expensive, e.g. Neathholm's traders, the Defender's Heart respec NPC.</summary>
        public static bool NeutralsIgnoreFog =>
            Settings.ModSettings.GetSetting<Settings.BoolSetting>("enhancements.neutrals_ignore_fog")?.Get() ?? true;

        /// <summary>Strategic hints (the J cycle / "Strategic" scan category): enemy spawn points and
        /// enemy objectives derived from the loaded scene (StrategicModel). A sighted player reads the
        /// gate an assault will pour through and the building the arsonists sprint for at a glance;
        /// the blind player had no channel for either (Defender's Heart tavern defense).</summary>
        public static bool StrategicHints =>
            Settings.ModSettings.GetSetting<Settings.BoolSetting>("enhancements.strategic_hints")?.Get() ?? true;

        /// <summary>Achievements stay enabled while mods are active (Patches/AchievementsPatch): the
        /// game refuses every unlock once any mod is loaded, and this mod is always loaded. Default
        /// on — an accessibility mod isn't a cheat.</summary>
        public static bool AchievementsWithMods =>
            Settings.ModSettings.GetSetting<Settings.BoolSetting>("enhancements.achievements_with_mods")?.Get() ?? true;

        /// <summary>Register the category + its settings (pre-load, with the other static categories).</summary>
        public static void RegisterSettings()
        {
            var enh = new Settings.CategorySetting("enhancements", "Enhancements",
                localizationKey: "category.enhancements");
            enh.Add(new Settings.BoolSetting("neutrals_ignore_fog", "Neutral NPCs ignore fog of war", true,
                "enh.neutrals_ignore_fog"));
            enh.Add(new Settings.BoolSetting("strategic_hints", "Strategic hints (spawn points, enemy objectives)", true,
                "enh.strategic_hints"));
            enh.Add(new Settings.BoolSetting("achievements_with_mods", "Achievements stay enabled with mods active", true,
                "enh.achievements_with_mods"));
            // The one-shot story awards the gate refused before the toggle existed: re-issue them
            // for every achievement whose award etude has already played (AchievementClaim).
            enh.Add(new Settings.ActionSetting("claim_achievements", "Claim achievements already earned",
                "enh.claim_achievements", AchievementClaim.Claim));
            Settings.ModSettings.Root.Add(enh);
        }
    }
}
