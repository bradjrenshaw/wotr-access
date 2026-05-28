namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Turns a <see cref="StatGroup"/> into a single navigable element (one Tab-stop). This is the seam
    /// for the character sheet's accessible presentation: the default <see cref="GridCharSheetLayout"/>
    /// renders tabular groups as a <see cref="Table"/> grid and single-value groups as a flat list, but
    /// a future layout (driven by a setting) can present the same stats differently — flat everywhere,
    /// everything gridded, regrouped — without changing how the sheet's data is assembled.
    /// </summary>
    public interface ICharSheetLayout
    {
        UIElement Build(StatGroup group);
    }
}
