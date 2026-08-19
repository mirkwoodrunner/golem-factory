using System.Linq;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Belt merges and splitters. The backlog recorded "no merge or splitter" as one gap; it was
    /// two, and only half of it was real -- merging already worked, because two belts pointing at
    /// the same cell both link to it and contend for its slots. Fan-out was the missing half: a
    /// belt has one facing, so it points at exactly one cell and cannot split by construction.
    /// </summary>
    public class BeltSplitterTests
    {
        private static BeltNetwork BuildNetwork(out ConveyorSystem conveyor)
        {
            conveyor = new ConveyorSystem();
            var network = new BeltNetwork();
            network.Configure(conveyor, null, 4);
            return network;
        }

        // --- merging (already worked; pinned so it stays working) ------------------------------

        [Test]
        public void TwoBeltsPointingAtOneCell_BothFeedIt()
        {
            BeltNetwork network = BuildNetwork(out _);
            network.TryPlace(new Vector2Int(0, 0), Facing.North, out PlacedBelt west);
            network.TryPlace(new Vector2Int(0, 2), Facing.South, out PlacedBelt east);
            network.TryPlace(new Vector2Int(0, 1), Facing.East, out PlacedBelt middle);

            Assert.AreSame(middle.Segment, west.Segment.Next, "the southern belt feeds it");
            Assert.AreSame(middle.Segment, east.Segment.Next, "and so does the northern one");
        }

        // --- splitting -------------------------------------------------------------------------

        [Test]
        public void ASplitterFeedsEveryNeighbourFacingAwayFromIt()
        {
            BeltNetwork network = BuildNetwork(out _);
            var splitterCell = new Vector2Int(0, 0);
            network.TryPlace(splitterCell, Facing.North, isSplitter: true, out PlacedBelt splitter);

            // Two belts leading away, one leading in, one running past sideways.
            network.TryPlace(new Vector2Int(0, 1), Facing.North, out PlacedBelt away);
            network.TryPlace(new Vector2Int(1, 0), Facing.East, out PlacedBelt alsoAway);
            network.TryPlace(new Vector2Int(0, -1), Facing.North, out PlacedBelt feedingIn);
            network.TryPlace(new Vector2Int(-1, 0), Facing.North, out PlacedBelt passingBy);

            Assert.AreEqual(2, splitter.Segment.Outputs.Count);
            Assert.Contains(away.Segment, (System.Collections.ICollection)splitter.Segment.Outputs);
            Assert.Contains(alsoAway.Segment, (System.Collections.ICollection)splitter.Segment.Outputs);

            Assert.AreSame(splitter.Segment, feedingIn.Segment.Next,
                "a belt pointing into the splitter feeds it, and is not an output");
            Assert.IsFalse(
                splitter.Segment.Outputs.Contains(passingBy.Segment),
                "a belt running past sideways is a neighbour and nothing more");
        }

        [Test]
        public void ASplitterAlternatesBetweenItsBranches()
        {
            // Round-robin rather than "always try the first": a fixed order would feed the left
            // branch until it backed up and only then use the right, which is a priority
            // splitter wearing a splitter's name.
            var splitter = new BeltSegment("Splitter", 4);
            var left = new BeltSegment("Left", 4);
            var right = new BeltSegment("Right", 4);
            splitter.AddOutput(left);
            splitter.AddOutput(right);

            for (int i = 0; i < 4; i++)
            {
                splitter.TryEnqueue(new ItemStack { ItemType = "Scrap" });
                splitter.Advance(4f);
                Assert.IsTrue(splitter.TryHandOff(), "handoff " + i);
                left.Advance(4f);
                right.Advance(4f);
            }

            Assert.AreEqual(2, left.Items.Count);
            Assert.AreEqual(2, right.Items.Count);
        }

        [Test]
        public void ABackedUpBranchDoesNotWasteTheSplittersTurn()
        {
            // The cursor advances only on a SUCCESSFUL handoff, so a jammed branch is skipped
            // rather than costing the item its slot -- otherwise one blocked lane halves the
            // throughput of a splitter that still has a clear one.
            var splitter = new BeltSegment("Splitter", 4);
            var blocked = new BeltSegment("Blocked", 1);
            var clear = new BeltSegment("Clear", 8);
            splitter.AddOutput(blocked);
            splitter.AddOutput(clear);

            // Fill the blocked branch so it refuses everything.
            blocked.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            for (int i = 0; i < 3; i++)
            {
                splitter.TryEnqueue(new ItemStack { ItemType = "Scrap" });
                splitter.Advance(4f);
                Assert.IsTrue(splitter.TryHandOff(), "the clear branch always takes it");
                clear.Advance(4f);
            }

            Assert.AreEqual(3, clear.Items.Count);
            Assert.AreEqual(1, blocked.Items.Count);
        }

        [Test]
        public void EveryBranchFull_ParksTheHeadAsBackpressure()
        {
            var splitter = new BeltSegment("Splitter", 4);
            var full = new BeltSegment("Full", 1);
            full.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            splitter.AddOutput(full);

            splitter.TryEnqueue(new ItemStack { ItemType = "Coke" });
            splitter.Advance(4f);

            Assert.IsFalse(splitter.TryHandOff());
            Assert.AreEqual(1, splitter.Items.Count, "the head stays parked, exactly as before");
        }

        [Test]
        public void ASplitterNeverFeedsAnotherSplitter()
        {
            // Two of them side by side have no facing between them, so "which way does this go"
            // would have no answer at all.
            BeltNetwork network = BuildNetwork(out _);
            network.TryPlace(new Vector2Int(0, 0), Facing.North, isSplitter: true, out PlacedBelt a);
            network.TryPlace(new Vector2Int(0, 1), Facing.North, isSplitter: true, out PlacedBelt b);

            Assert.AreEqual(0, a.Segment.Outputs.Count);
            Assert.AreEqual(0, b.Segment.Outputs.Count);
        }

        [Test]
        public void RemovingASplitter_LeavesItsBranchesUnlinked()
        {
            BeltNetwork network = BuildNetwork(out _);
            var cell = new Vector2Int(0, 0);
            network.TryPlace(cell, Facing.North, isSplitter: true, out PlacedBelt splitter);
            network.TryPlace(new Vector2Int(0, 1), Facing.North, out PlacedBelt away);

            Assert.AreEqual(1, splitter.Segment.Outputs.Count);

            Assert.IsTrue(network.TryRemove(cell));

            // ...and the cell can be reused as an ordinary belt, with no ghost of the splitter.
            network.TryPlace(cell, Facing.North, out PlacedBelt plain);
            Assert.AreSame(away.Segment, plain.Segment.Next);
            Assert.AreEqual(1, plain.Segment.Outputs.Count, "a plain belt feeds exactly one cell");
        }

        // --- the single-output world is unchanged ---------------------------------------------

        [Test]
        public void AnOrdinaryBeltStillHasExactlyOneNext()
        {
            var upstream = new BeltSegment("A", 4);
            var downstream = new BeltSegment("B", 4);

            upstream.Next = downstream;

            Assert.AreSame(downstream, upstream.Next);
            Assert.AreEqual(1, upstream.Outputs.Count);

            upstream.Next = null;
            Assert.IsNull(upstream.Next);
            Assert.AreEqual(0, upstream.Outputs.Count, "clearing Next clears the fan-out");
        }

        [Test]
        public void ASegmentNeverFeedsItselfOrTakesADuplicate()
        {
            var segment = new BeltSegment("A", 4);
            var other = new BeltSegment("B", 4);

            Assert.IsFalse(segment.AddOutput(segment), "a self-link is an instant one-cycle");
            Assert.IsTrue(segment.AddOutput(other));
            Assert.IsFalse(segment.AddOutput(other), "and a duplicate would double its share");
            Assert.AreEqual(1, segment.Outputs.Count);
        }
    }
}
