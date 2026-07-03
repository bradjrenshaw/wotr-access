using System;
using System.Collections.Generic;
using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    public class KeyGraphTests
    {
        private static NodeVtable Vt(string label) => new NodeVtable { Label = () => label };
        private static ControlId Id(string key) => ControlId.Structural(key);

        private static KeyGraph Menu(GraphState state, params string[] items)
            => new KeyGraph(() =>
            {
                var b = new GraphBuilder();
                foreach (var i in items) b.AddItem(Id(i), Vt(i));
                return b.Build();
            }, state);

        [Fact]
        public void FirstRenderLandsOnStart()
        {
            var state = new GraphState();
            var g = Menu(state, "a", "b");
            Assert.True(g.Rerender());
            Assert.Equal(Id("a"), state.CurKey);
        }

        [Fact]
        public void MoveStepsAndStopsAtEdges()
        {
            var state = new GraphState();
            var g = Menu(state, "a", "b");

            var r = g.Move(GraphDir.Down);
            Assert.True(r.Moved);
            Assert.Equal(Id("b"), r.To.Id);

            r = g.Move(GraphDir.Down); // at the end
            Assert.False(r.Moved);
            Assert.Equal(Id("b"), r.To.Id);
            Assert.Same(r.From, r.To);
        }

        [Fact]
        public void MoveToEdgeGoesAllTheWay()
        {
            var state = new GraphState();
            var g = Menu(state, "a", "b", "c", "d");
            var r = g.MoveToEdge(GraphDir.Down);
            Assert.True(r.Moved);
            Assert.Equal(Id("d"), r.To.Id);
        }

        [Fact]
        public void TransitionLabelIsReported()
        {
            var state = new GraphState();
            var g = new KeyGraph(() => new GraphBuilder()
                .AddNode(Id("a"), Vt("A")).AddNode(Id("b"), Vt("B"))
                .Connect(Id("a"), GraphDir.Right, Id("b"), "lane change")
                .Build(), state);

            var r = g.Move(GraphDir.Right);
            Assert.True(r.Moved);
            Assert.Equal("lane change", r.TransitionLabel);
        }

        [Fact]
        public void ReconcileTier2FollowsStructuralKeyAcrossRebuilds()
        {
            var state = new GraphState();
            int generation = 0;
            var g = new KeyGraph(() =>
            {
                generation++; // fresh vtables/nodes each render — only the structural keys repeat
                return new GraphBuilder()
                    .AddItem(Id("a"), Vt("A" + generation))
                    .AddItem(Id("b"), Vt("B" + generation))
                    .Build();
            }, state);

            g.Move(GraphDir.Down);
            Assert.Equal(Id("b"), state.CurKey);
            Assert.True(g.Rerender()); // a whole new render
            Assert.Equal(Id("b"), state.CurKey);
        }

        [Fact]
        public void ReconcileTier1FollowsAMovedObject()
        {
            var state = new GraphState();
            var thing = new object(); // the backing domain object
            int slot = 1;             // its structural position, which will change

            var g = new KeyGraph(() => new GraphBuilder()
                .AddItem(ControlId.Structural("header"), Vt("Header"))
                .AddItem(ControlId.Referenced(thing, "slot" + slot), Vt("Thing"))
                .Build(), state);

            g.Move(GraphDir.Down); // focus the thing (at slot1)
            Assert.Equal("slot1", state.CurKey.StructuralKey);

            slot = 2; // the object moves to a different slot
            Assert.True(g.Rerender());
            Assert.Equal("slot2", state.CurKey.StructuralKey); // followed the reference, not the old key
        }

        [Fact]
        public void ReconcileFallsBackToNearestSurvivor()
        {
            var state = new GraphState();
            var items = new List<string> { "a", "b", "c", "d" };
            var g = new KeyGraph(() =>
            {
                var b = new GraphBuilder();
                foreach (var i in items) b.AddItem(Id(i), Vt(i));
                return b.Build();
            }, state);

            g.MoveToEdge(GraphDir.Down); // on "d"
            items.Remove("d");
            items.Remove("c");
            Assert.True(g.Rerender());
            Assert.Equal(Id("b"), state.CurKey); // nearest earlier survivor
        }

        [Fact]
        public void SuggestedMoveIsHonoredAndConsumed()
        {
            var state = new GraphState();
            var g = Menu(state, "a", "b", "c");
            state.NextSuggestedMove = Id("c");
            Assert.True(g.Rerender());
            Assert.Equal(Id("c"), state.CurKey);
            Assert.Null(state.NextSuggestedMove);
        }

        [Fact]
        public void ComputeOrderCoversAllStops()
        {
            var state = new GraphState();
            var g = new KeyGraph(() => new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .BeginStop().AddItem(Id("b"), Vt("B"))
                .BeginStop().AddItem(Id("c"), Vt("C"))
                .Build(), state);

            Assert.True(g.Rerender());
            Assert.Equal(3, state.KeyOrder.Count); // later stops appended despite no cross-stop edges
        }

        [Fact]
        public void StopCyclingRemembersPositionPerStop()
        {
            var state = new GraphState();
            var g = new KeyGraph(() => new GraphBuilder()
                .AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2"))
                .BeginStop()
                .AddItem(Id("b1"), Vt("B1")).AddItem(Id("b2"), Vt("B2"))
                .Build(), state);

            g.Move(GraphDir.Down); // a2 (remembered for stop 1)
            var r = g.MoveStop(+1, wrap: false);
            Assert.True(r.Moved);
            Assert.Equal(Id("b1"), r.To.Id);
            g.Move(GraphDir.Down); // b2

            r = g.MoveStop(-1, wrap: false);
            Assert.Equal(Id("a2"), r.To.Id); // remembered, not the stop's first node

            r = g.MoveStop(-1, wrap: false); // at the first stop, no wrap
            Assert.False(r.Moved);

            r = g.MoveStop(-1, wrap: true); // wraps to the last stop's memory
            Assert.True(r.Moved);
            Assert.Equal(Id("b2"), r.To.Id);
        }

        [Fact]
        public void RegionJumpsWithinStop()
        {
            var state = new GraphState();
            var g = new KeyGraph(() => new GraphBuilder()
                .SetRegion("filters").AddItem(Id("f1"), Vt("F1"))
                .SetRegion("items").AddItem(Id("i1"), Vt("I1")).AddItem(Id("i2"), Vt("I2"))
                .SetRegion("footer").AddItem(Id("z1"), Vt("Z1"))
                .Build(), state);

            var r = g.MoveRegion(+1);
            Assert.Equal(Id("i1"), r.To.Id);
            r = g.MoveRegion(+1);
            Assert.Equal(Id("z1"), r.To.Id);
            r = g.MoveRegion(+1); // at the last region
            Assert.False(r.Moved);
            r = g.MoveRegion(-1);
            Assert.Equal(Id("i1"), r.To.Id);
        }

        [Fact]
        public void BehaviorInvokersReportAbsence()
        {
            var state = new GraphState();
            bool clicked = false, adjusted = false;
            var g = new KeyGraph(() => new GraphBuilder()
                .AddItem(Id("a"), new NodeVtable
                {
                    Label = () => "A",
                    OnActivate = () => clicked = true,
                    OnAdjust = (sign, large) => adjusted = sign > 0,
                })
                .Build(), state);

            Assert.True(g.Activate());
            Assert.True(clicked);
            Assert.True(g.TryAdjust(+1, false));
            Assert.True(adjusted);
            Assert.False(g.Secondary());
            Assert.False(g.Tooltip());
        }

        [Fact]
        public void FocusAndFocusByReferenceWork()
        {
            var state = new GraphState();
            var backing = new object();
            var g = new KeyGraph(() => new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .AddItem(ControlId.Referenced(backing, "b"), Vt("B"))
                .Build(), state);

            Assert.True(g.Rerender());
            Assert.True(g.FocusByReference(backing));
            Assert.Equal("b", state.CurKey.StructuralKey);
            Assert.False(g.FocusByReference(backing)); // already there — not a change

            Assert.True(g.Focus(Id("a")));
            Assert.Equal(Id("a"), state.CurKey);
            Assert.False(g.Focus(Id("nope")));
        }
    }
}
