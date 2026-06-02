using System.Collections.Generic;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Where the character sheet's sections are written. The sheet's data assembly (which CharInfo* VMs,
    /// which groups) is the same regardless of presentation; the sink decides the shape — a panel of
    /// separate Tab-stops (<see cref="PanelCharSheetSink"/>) or one unified grid
    /// (<see cref="FlowSheetCharSheetSink"/>). Two section kinds: column-aligned stat groups, and
    /// free-form lists (Summary, Attack, Features, …). Empty groups/sections are skipped. <see cref="Build"/>
    /// returns the finished root element to add to the screen. Generalizes the old per-group
    /// <c>ICharSheetLayout</c> to the whole sheet, and is shared by the chargen Total phase and (later)
    /// the in-game character window.
    /// </summary>
    public interface ICharSheetSink
    {
        void StatGroup(StatGroup group);
        void ListSection(string label, IEnumerable<UIElement> items);
        UIElement Build();
    }
}
