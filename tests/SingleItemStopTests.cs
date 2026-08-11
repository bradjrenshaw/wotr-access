using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    // Repro for the user report: tabbing into a labeled list that holds a SINGLE item skipped the
    // list's context (spoke just the item) — the loot-window case. The context chain must announce
    // regardless of how many items the stop holds.
    public class SingleItemStopTests
    {
        private static NodeVtable Vt(string label) => new NodeVtable { Announcements = new[] { NodeAnnouncement.Static(label) } };
        private static ControlId Id(string key) => ControlId.Structural(key);

        private static GraphRender TwoStops(int itemsInB)
        {
            var b = new GraphBuilder();
            b.BeginStop("a").AddItem(Id("back"), Vt("Back"));
            b.BeginStop("b").PushContext("Corpse of Mongrel", "list");
            for (int i = 0; i < itemsInB; i++)
                b.AddItem(Id("item" + i), Vt("Longsword " + i));
            b.PopContext();
            return b.Build();
        }

        [Fact]
        public void TabIntoMultiItemStopSpeaksContext()
        {
            var render = TwoStops(2);
            var from = render.Nodes[Id("back")];
            var land = KeyGraph.StopLanding(render, new GraphState(), "b");
            Assert.Equal(Id("item0"), land.Id);
            Assert.Equal("Corpse of Mongrel, list, Longsword 0, 1 of 2",
                WithPositions(() => GraphAnnouncer.Compose(from, land)));
        }

        [Fact]
        public void TabIntoSingleItemStopSpeaksContext()
        {
            var render = TwoStops(1);
            var from = render.Nodes[Id("back")];
            var land = KeyGraph.StopLanding(render, new GraphState(), "b");
            Assert.Equal(Id("item0"), land.Id);
            Assert.Equal("Corpse of Mongrel, list, Longsword 0",
                WithPositions(() => GraphAnnouncer.Compose(from, land)));
        }

        [Fact]
        public void TabBetweenSameNamedContextsSpeaksTheNewContext()
        {
            // Two containers with the SAME display name (two mongrel corpses), one item each — the
            // label-pathed ctx ids collide, the prefix diff thinks focus never left the first
            // container, and tabbing speaks just the bare item.
            var b = new GraphBuilder();
            b.BeginStop("s1").PushContext("Corpse of Mongrel", "list");
            b.AddItem(Id("i1"), Vt("Longsword"));
            b.PopContext();
            b.BeginStop("s2").PushContext("Corpse of Mongrel", "list");
            b.AddItem(Id("i2"), Vt("Dagger"));
            b.PopContext();
            var render = b.Build();

            var from = render.Nodes[Id("i1")];
            var land = KeyGraph.StopLanding(render, new GraphState(), "s2");
            Assert.Equal(Id("i2"), land.Id);
            Assert.Equal("Corpse of Mongrel, list, Dagger",
                WithPositions(() => GraphAnnouncer.Compose(from, land)));
        }

        private static string WithPositions(System.Func<string> compose)
        {
            var prev = GraphAnnouncer.PositionText;
            GraphAnnouncer.PositionText = (i, n) => i + " of " + n;
            try { return compose(); }
            finally { GraphAnnouncer.PositionText = prev; }
        }
    }
}
