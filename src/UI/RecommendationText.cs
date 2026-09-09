using Kingmaker.UI.Common;              // UIUtility.GetGlossaryEntry
using Kingmaker.UI.MVVM._VM.Other;      // RecommendationType

namespace WrathAccess.UI
{
    /// <summary>
    /// The spoken form of the game's recommendation marker (the thumbs-up / thumbs-down icon beside
    /// feats, spells and ability scores in level-up): the GAME's own words, read from the glossary
    /// entries the sighted icon's tooltip links to ("RecommendedFeature" / "NotRecommendedFeature" —
    /// "Recommended feature" / "Non-recommended feature" in English), so every language the game
    /// ships gets its own text. Neutral shows no marker and so speaks nothing.
    /// </summary>
    internal static class RecommendationText
    {
        public static string Label(RecommendationType type)
        {
            switch (type)
            {
                case RecommendationType.Recommended: return Glossary("RecommendedFeature");
                case RecommendationType.NotRecommended: return Glossary("NotRecommendedFeature");
                default: return null;
            }
        }

        private static string Glossary(string key)
        {
            try
            {
                var entry = UIUtility.GetGlossaryEntry(key);
                var name = entry?.Name?.ToString();
                return string.IsNullOrEmpty(name) ? key : TextUtil.StripRichText(name);
            }
            catch { return key; }
        }
    }
}
