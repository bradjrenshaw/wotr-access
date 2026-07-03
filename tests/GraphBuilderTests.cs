using System;
using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    public class GraphBuilderTests
    {
        private static NodeVtable Vt(string label) => new NodeVtable { Label = () => label };
        private static ControlId Id(string key) => ControlId.Structural(key);

        [Fact]
        public void SingleItemsFormVerticalMenu()
        {
            var render = new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .AddItem(Id("b"), Vt("B"))
                .AddItem(Id("c"), Vt("C"))
                .Build();

            Assert.Equal(Id("a"), render.StartKey);
            Assert.Equal(Id("b"), render.Nodes[Id("a")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("a"), render.Nodes[Id("b")].Transitions[GraphDir.Up].Destination);
            Assert.Equal(Id("c"), render.Nodes[Id("b")].Transitions[GraphDir.Down].Destination);
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Up));
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Left));
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Right));
        }

        [Fact]
        public void RowsWireHorizontally()
        {
            var render = new GraphBuilder()
                .StartRow().AddItem(Id("a"), Vt("A")).AddItem(Id("b"), Vt("B")).EndRow()
                .Build();

            Assert.Equal(Id("b"), render.Nodes[Id("a")].Transitions[GraphDir.Right].Destination);
            Assert.Equal(Id("a"), render.Nodes[Id("b")].Transitions[GraphDir.Left].Destination);
        }

        [Fact]
        public void SharedRowKeysPreserveColumn()
        {
            var render = new GraphBuilder()
                .StartRow("grid").AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).EndRow()
                .StartRow("grid").AddItem(Id("b1"), Vt("B1")).AddItem(Id("b2"), Vt("B2")).EndRow()
                .Build();

            Assert.Equal(Id("b2"), render.Nodes[Id("a2")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("a2"), render.Nodes[Id("b2")].Transitions[GraphDir.Up].Destination);
        }

        [Fact]
        public void UnkeyedRowsLandOnFirstItem()
        {
            var render = new GraphBuilder()
                .StartRow().AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).EndRow()
                .StartRow().AddItem(Id("b1"), Vt("B1")).AddItem(Id("b2"), Vt("B2")).EndRow()
                .Build();

            Assert.Equal(Id("b1"), render.Nodes[Id("a2")].Transitions[GraphDir.Down].Destination);
        }

        [Fact]
        public void RaggedKeyedRowFallsToFirstItem()
        {
            var render = new GraphBuilder()
                .StartRow("grid").AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).AddItem(Id("a3"), Vt("A3")).EndRow()
                .StartRow("grid").AddItem(Id("b1"), Vt("B1")).EndRow()
                .Build();

            // Column 2 doesn't exist below → first item.
            Assert.Equal(Id("b1"), render.Nodes[Id("a3")].Transitions[GraphDir.Down].Destination);
        }

        [Fact]
        public void ArrowsNeverCrossStops()
        {
            var render = new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .BeginStop()
                .AddItem(Id("b"), Vt("B"))
                .Build();

            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Down));
            Assert.False(render.Nodes[Id("b")].Transitions.ContainsKey(GraphDir.Up));
            Assert.NotEqual(render.Nodes[Id("a")].StopKey, render.Nodes[Id("b")].StopKey);
        }

        [Fact]
        public void ContextChainIsCapturedPerNode()
        {
            var render = new GraphBuilder()
                .PushContext("Settings", "list")
                .AddItem(Id("a"), Vt("A"))
                .PushContext("Advanced", "group")
                .AddItem(Id("b"), Vt("B"))
                .PopContext()
                .AddItem(Id("c"), Vt("C"))
                .Build();

            Assert.Single(render.Nodes[Id("a")].Context);
            Assert.Equal(2, render.Nodes[Id("b")].Context.Count);
            Assert.Equal("Advanced", render.Nodes[Id("b")].Context[1].Label);
            Assert.Single(render.Nodes[Id("c")].Context);
        }

        [Fact]
        public void RegionsAreStamped()
        {
            var render = new GraphBuilder()
                .SetRegion("filters").AddItem(Id("a"), Vt("A"))
                .SetRegion("items").AddItem(Id("b"), Vt("B"))
                .Build();

            Assert.Equal("filters", render.Nodes[Id("a")].RegionKey);
            Assert.Equal("items", render.Nodes[Id("b")].RegionKey);
        }

        [Fact]
        public void RawModeWiresExplicitEdges()
        {
            var render = new GraphBuilder()
                .AddNode(Id("a"), Vt("A"))
                .AddNode(Id("b"), Vt("B"))
                .Connect(Id("a"), GraphDir.Right, Id("b"), "crossing the aisle")
                .Connect(Id("a"), GraphDir.Down, Id("ghost")) // undeclared → dropped
                .SetStart(Id("b"))
                .Build();

            Assert.Equal(Id("b"), render.StartKey);
            Assert.Equal("crossing the aisle", render.Nodes[Id("a")].Transitions[GraphDir.Right].Label);
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Down));
        }

        [Fact]
        public void GuardsRejectMisuse()
        {
            Assert.Null(new GraphBuilder().Build()); // empty = closed

            var dup = new GraphBuilder().AddItem(Id("a"), Vt("A"));
            Assert.Throws<InvalidOperationException>(() => dup.AddItem(Id("a"), Vt("A2")));

            Assert.Throws<ArgumentException>(() => new GraphBuilder().AddItem(Id("x"), new NodeVtable()));
        }

        [Fact]
        public void MenuRowsAndRawNodesMix()
        {
            // A screen mixing an auto-wired list with a computed-topology grid: raw edges may
            // reference menu nodes.
            var render = new GraphBuilder()
                .AddItem(Id("list1"), Vt("List1"))
                .AddNode(Id("cell1"), Vt("Cell1"))
                .AddNode(Id("cell2"), Vt("Cell2"))
                .Connect(Id("cell1"), GraphDir.Right, Id("cell2"))
                .Connect(Id("cell1"), GraphDir.Up, Id("list1"))
                .Build();

            Assert.Equal(3, render.Order.Count);
            Assert.Equal(Id("cell2"), render.Nodes[Id("cell1")].Transitions[GraphDir.Right].Destination);
            Assert.Equal(Id("list1"), render.Nodes[Id("cell1")].Transitions[GraphDir.Up].Destination);
        }
    }
}
