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

        /// <summary>Register the category + its settings (pre-load, with the other static categories).</summary>
        public static void RegisterSettings()
        {
            var enh = new Settings.CategorySetting("enhancements", "Enhancements",
                localizationKey: "category.enhancements");
            enh.Add(new Settings.BoolSetting("neutrals_ignore_fog", "Neutral NPCs ignore fog of war", true,
                "enh.neutrals_ignore_fog"));
            enh.Add(new Settings.BoolSetting("strategic_hints", "Strategic hints (spawn points, enemy objectives)", true,
                "enh.strategic_hints"));
            Settings.ModSettings.Root.Add(enh);
        }
    }
}
