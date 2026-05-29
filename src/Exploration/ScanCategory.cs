namespace WrathAccess.Exploration
{
    /// <summary>
    /// A scanner category. An item can belong to several at once (an entity reports all that apply —
    /// e.g. a lootable corpse is both Enemies and Containers), so categorization is many-to-many.
    /// Points of Interest is sourced from the local-map markers (the game's curated list) so we can
    /// compare it against what we derive from the entity pools.
    /// </summary>
    internal enum ScanCategory
    {
        PointsOfInterest,
        Party,
        Enemies,
        Neutrals,
        Doors,
        Containers,
        Exits,
        SearchPoints,
        Other,
    }

    internal static class ScanCategories
    {
        /// <summary>Display + cycle order for Ctrl+PageUp/Down.</summary>
        public static readonly ScanCategory[] Order =
        {
            ScanCategory.PointsOfInterest, ScanCategory.Party, ScanCategory.Enemies, ScanCategory.Neutrals,
            ScanCategory.Doors, ScanCategory.Containers, ScanCategory.Exits, ScanCategory.SearchPoints, ScanCategory.Other,
        };

        public static string Label(ScanCategory c)
        {
            switch (c)
            {
                case ScanCategory.PointsOfInterest: return "Points of Interest";
                case ScanCategory.SearchPoints: return "Search Points";
                case ScanCategory.Other: return "Other Interactables";
                default: return c.ToString();
            }
        }

        /// <summary>Singular label used to name an otherwise-unnamed map object (e.g. "Door").</summary>
        public static string Singular(ScanCategory c)
        {
            switch (c)
            {
                case ScanCategory.Doors: return "Door";
                case ScanCategory.Containers: return "Container";
                case ScanCategory.Exits: return "Exit";
                case ScanCategory.SearchPoints: return "Search point";
                default: return "Object";
            }
        }
    }
}
