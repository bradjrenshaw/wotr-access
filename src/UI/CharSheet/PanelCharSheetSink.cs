using System.Collections.Generic;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// The original presentation: each section is its own Tab-stop in a panel — stat groups via
    /// <see cref="GridCharSheetLayout"/> (table or flat list), free-form sections as a
    /// <see cref="ListContainer"/>. Kept as the fallback / selectable alternative to
    /// <see cref="FlowSheetCharSheetSink"/>. NOT currently instantiated anywhere — reachable only
    /// by swapping the sink in TotalPhaseContent / CharacterInfoScreen (or a future presentation
    /// setting); this chain (with GridCharSheetLayout + Table) is retained deliberately.
    /// </summary>
    public sealed class PanelCharSheetSink : ICharSheetSink
    {
        private readonly Panel _panel = new Panel();
        private readonly ICharSheetLayout _layout = new GridCharSheetLayout();

        public void StatGroup(StatGroup g)
        {
            if (g == null || g.Rows.Count == 0) return;
            _panel.Add(_layout.Build(g));
        }

        public void ListSection(string label, IEnumerable<UIElement> items)
        {
            if (items == null) return;
            ListContainer list = null;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (list == null) list = new ListContainer(label);
                list.Add(it);
            }
            if (list != null) _panel.Add(list);
        }

        public UIElement Build() => _panel;
    }
}
