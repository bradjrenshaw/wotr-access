using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    public class GraphAnnouncerTests
    {
        private static GraphNode Node(string label, params ContextEntry[] context) => new GraphNode
        {
            Id = ControlId.Structural(label),
            Vtable = new NodeVtable { Announcements = new[] { NodeAnnouncement.Static(label) } },
            Context = context,
        };

        [Fact]
        public void EntryFromNothingReadsFullChain()
        {
            var node = Node("Normal, radio button, selected",
                new ContextEntry("Options"), new ContextEntry("Difficulty settings", "list"));

            Assert.Equal("Options, Difficulty settings, list, Normal, radio button, selected",
                GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void SiblingMoveReadsLeafOnly()
        {
            var ctx = new ContextEntry("Difficulty settings", "list");
            var from = Node("Easy", ctx);
            var to = Node("Hard", ctx);

            Assert.Equal("Hard", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void EnteringNestedContextReadsEnteredLevels()
        {
            var outer = new ContextEntry("Options");
            var from = Node("Back", outer);
            var to = Node("Normal", outer, new ContextEntry("Difficulty settings", "list"));

            Assert.Equal("Difficulty settings, list, Normal", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void AscendReadsLeafOnly()
        {
            var outer = new ContextEntry("Options");
            var from = Node("Normal", outer, new ContextEntry("Difficulty settings", "list"));
            var to = Node("Back", outer);

            Assert.Equal("Back", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void DuplicateContainerLabelIsSkipped()
        {
            // A "Game difficulty" section wrapping the "Game difficulty" control: the section stays silent.
            var to = Node("Game difficulty, menu button", new ContextEntry("Game difficulty", "group"));
            Assert.Equal("Game difficulty, menu button", GraphAnnouncer.ComposeFull(to));

            // But a control that merely STARTS with different text keeps its container.
            var other = Node("Game difficulty presets, menu button", new ContextEntry("Game difficulty", "group"));
            Assert.Equal("Game difficulty, group, Game difficulty presets, menu button",
                GraphAnnouncer.ComposeFull(other));
        }

        [Fact]
        public void LeafTextJoinsAnnouncementParts()
        {
            var node = new GraphNode
            {
                Id = ControlId.Structural("x"),
                Vtable = new NodeVtable
                {
                    Announcements = new[]
                    {
                        NodeAnnouncement.Static("Hold position"),
                        NodeAnnouncement.Static("toggle"),
                        new NodeAnnouncement(() => "on", live: true),
                        new NodeAnnouncement(() => null), // empty at speak time — silent
                    },
                },
            };
            Assert.Equal("Hold position, toggle, on", GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void TransitionLabelLeads()
        {
            var from = Node("A");
            var to = Node("B");
            Assert.Equal("next column, B", GraphAnnouncer.Compose(from, to, "next column"));
        }

        [Fact]
        public void ContextChangeAtSameDepthReadsNewLevel()
        {
            var from = Node("Fireball", new ContextEntry("Level 1 spells", "table"));
            var to = Node("Haste", new ContextEntry("Level 2 spells", "table"));

            Assert.Equal("Level 2 spells, table, Haste", GraphAnnouncer.Compose(from, to));
        }
    }
}
