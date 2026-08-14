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
        /// <summary>Revealed neutral/bystander NPCs stay locatable on the MAP while fogged (the
        /// sighted map hides fogged units entirely — but re-finding a met vendor by sweeping an
        /// area is sighted-cheap and blind-expensive, e.g. Neathholm's traders).</summary>
        public static bool NeutralsIgnoreFog =>
            Settings.ModSettings.GetSetting<Settings.BoolSetting>("enhancements.neutrals_ignore_fog")?.Get() ?? true;

        /// <summary>Register the category + its settings (pre-load, with the other static categories).</summary>
        public static void RegisterSettings()
        {
            var enh = new Settings.CategorySetting("enhancements", "Enhancements",
                localizationKey: "category.enhancements");
            enh.Add(new Settings.BoolSetting("neutrals_ignore_fog", "Neutral NPCs ignore fog of war", true,
                "enh.neutrals_ignore_fog"));
            Settings.ModSettings.Root.Add(enh);
        }
    }
}
