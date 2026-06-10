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
        All,
        PointsOfInterest,
        Party,
        Enemies,
        Neutrals,
        Doors,
        Containers,
        Exits,
        SearchPoints,
        Traps,
        Other,
        Scenery,
    }

    internal static class ScanCategories
    {
        /// <summary>Display + cycle order for Ctrl+PageUp/Down.</summary>
        public static readonly ScanCategory[] Order =
        {
            ScanCategory.All,
            ScanCategory.PointsOfInterest, ScanCategory.Party, ScanCategory.Enemies, ScanCategory.Neutrals,
            ScanCategory.Doors, ScanCategory.Containers, ScanCategory.Exits, ScanCategory.SearchPoints,
            ScanCategory.Traps, ScanCategory.Other, ScanCategory.Scenery,
        };

        public static string Label(ScanCategory c)
            => Loc.T("scan.category." + c.ToString().ToLowerInvariant());

        /// <summary>Singular label used to name an otherwise-unnamed map object (e.g. "Door").</summary>
        public static string Singular(ScanCategory c)
        {
            switch (c)
            {
                case ScanCategory.Doors: return Loc.T("scan.singular.door");
                case ScanCategory.Containers: return Loc.T("scan.singular.container");
                case ScanCategory.Exits: return Loc.T("scan.singular.exit");
                case ScanCategory.SearchPoints: return Loc.T("scan.singular.search_point");
                case ScanCategory.Traps: return Loc.T("scan.singular.trap");
                default: return Loc.T("scan.singular.object");
            }
        }
    }
}
