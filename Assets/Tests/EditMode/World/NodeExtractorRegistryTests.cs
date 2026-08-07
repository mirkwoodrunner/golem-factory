using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The 2-extractor-per-node cap (docs/progression-design.md §3.2).
    //
    // The mechanic is a throughput ceiling, so it only means anything if the SAME two golems
    // keep working. Every test here is really about that: an unstable refusal would make three
    // golems on one seam behave like three golems at two-thirds rate rather than like two
    // golems and one clearly-stalled one, and the player would read the cap as a mystery
    // slowdown instead of a rule.
    public class NodeExtractorRegistryTests
    {
        [Test]
        public void TwoGolemsMayWorkOneNode()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");

            Assert.IsTrue(registry.IsWorking("CoalNode", "A"));
            Assert.IsTrue(registry.IsWorking("CoalNode", "B"));
            Assert.AreEqual(2, registry.CrewCount("CoalNode"));
        }

        [Test]
        public void AThirdGolemIsRefused()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");
            registry.RegisterExtractor("C", new Vector2Int(1, 0), "CoalNode");

            Assert.AreEqual(NodeExtractorRegistry.MaxExtractorsPerNode, registry.CrewCount("CoalNode"));
            Assert.IsFalse(registry.IsWorking("CoalNode", "C"));
        }

        // WHICH two is decided by cell order (World.CellOrder), not by who registered first.
        // Registration order is the tempting answer and is exactly what §1.4's steam notes
        // reject: two identically-built factories assembled in a different order would crew
        // different golems, and a save/load would crew a third set.
        [Test]
        public void TheRefusedGolemIsChosenByCellOrderNotRegistrationOrder()
        {
            var registry = new NodeExtractorRegistry();

            // Registered worst-cell-first, deliberately.
            registry.RegisterExtractor("Late", new Vector2Int(5, 5), "CoalNode");
            registry.RegisterExtractor("Middle", new Vector2Int(1, 0), "CoalNode");
            registry.RegisterExtractor("First", new Vector2Int(0, 0), "CoalNode");

            Assert.IsTrue(registry.IsWorking("CoalNode", "First"));
            Assert.IsTrue(registry.IsWorking("CoalNode", "Middle"));
            Assert.IsFalse(registry.IsWorking("CoalNode", "Late"));
        }

        // Column-major (x then y), same total order the steam network uses.
        [Test]
        public void CellOrderIsColumnMajorAndTotal()
        {
            Assert.Less(CellOrder.Compare(new Vector2Int(0, 9), new Vector2Int(1, 0)), 0);
            Assert.Less(CellOrder.Compare(new Vector2Int(2, 3), new Vector2Int(2, 4)), 0);
            Assert.AreEqual(0, CellOrder.Compare(new Vector2Int(2, 3), new Vector2Int(2, 3)));
        }

        // Ties on cell cannot happen in the shipped game (GridMap allows one occupant per cell)
        // but the id tiebreak is what makes the order TOTAL: a comparison returning 0 for two
        // distinct claims hands the decision to List.Sort's unstable introsort.
        [Test]
        public void GolemIdBreaksACellTieDeterministically()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("zeta", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("alpha", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("mid", new Vector2Int(0, 0), "CoalNode");

            Assert.IsTrue(registry.IsWorking("CoalNode", "alpha"));
            Assert.IsTrue(registry.IsWorking("CoalNode", "mid"));
            Assert.IsFalse(registry.IsWorking("CoalNode", "zeta"));
        }

        // The refusal must be STABLE. A golem that stalls this tick and works the next has
        // turned a cap into a round-robin, which is not what §3.2 asks for and is untestable
        // in a live factory.
        [Test]
        public void TheRefusalIsStableAcrossRepeatedAsks()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");
            registry.RegisterExtractor("C", new Vector2Int(1, 0), "CoalNode");

            for (int tick = 0; tick < 50; tick++)
            {
                // A stalled golem re-registers on every retry -- that is how it applies -- so
                // the repeated idempotent registration is part of what is being tested.
                registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
                registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");
                registry.RegisterExtractor("C", new Vector2Int(1, 0), "CoalNode");

                Assert.IsTrue(registry.IsWorking("CoalNode", "A"), "tick " + tick);
                Assert.IsTrue(registry.IsWorking("CoalNode", "B"), "tick " + tick);
                Assert.IsFalse(registry.IsWorking("CoalNode", "C"), "tick " + tick);
            }
        }

        [Test]
        public void TheCapIsPerNodeNotGlobal()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");
            registry.RegisterExtractor("C", new Vector2Int(1, 0), "ScrapNode");
            registry.RegisterExtractor("D", new Vector2Int(1, 1), "ScrapNode");

            Assert.AreEqual(2, registry.CrewCount("CoalNode"));
            Assert.AreEqual(2, registry.CrewCount("ScrapNode"));
            Assert.IsTrue(registry.IsWorking("ScrapNode", "C"));
            Assert.IsTrue(registry.IsWorking("ScrapNode", "D"));
        }

        // A claim that outlived the golem holding it would permanently under-crew a seam with
        // nothing on the tile to explain why. This is what GolemEntity.ReleaseNodeClaim exists
        // for -- on disable, on being picked up, and on being re-placed.
        [Test]
        public void ReleasingAClaimPromotesTheRefusedGolem()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(0, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(0, 1), "CoalNode");
            registry.RegisterExtractor("C", new Vector2Int(1, 0), "CoalNode");

            Assert.IsFalse(registry.IsWorking("CoalNode", "C"));

            Assert.IsTrue(registry.UnregisterExtractor("A"));

            Assert.IsTrue(registry.IsWorking("CoalNode", "B"));
            Assert.IsTrue(registry.IsWorking("CoalNode", "C"));
            Assert.IsFalse(registry.IsWorking("CoalNode", "A"));
        }

        // Moving a golem to a better cell must take a working golem's place, because the order
        // is over CELLS -- that is the whole reason cell order was chosen over arrival order.
        [Test]
        public void MovingAGolemReSortsTheCrew()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor("A", new Vector2Int(3, 0), "CoalNode");
            registry.RegisterExtractor("B", new Vector2Int(4, 0), "CoalNode");
            registry.RegisterExtractor("C", new Vector2Int(9, 0), "CoalNode");

            Assert.IsFalse(registry.IsWorking("CoalNode", "C"));

            registry.RegisterExtractor("C", new Vector2Int(0, 0), "CoalNode");

            Assert.IsTrue(registry.IsWorking("CoalNode", "C"));
            Assert.IsTrue(registry.IsWorking("CoalNode", "A"));
            Assert.IsFalse(registry.IsWorking("CoalNode", "B"));
        }

        // Same null-id discipline every other registry in the project follows: an unset id is
        // a false, never an exception inside a tick.
        [Test]
        public void NullAndUnknownIdsAreRefusedWithoutThrowing()
        {
            var registry = new NodeExtractorRegistry();
            registry.RegisterExtractor(null, Vector2Int.zero, "CoalNode");
            registry.RegisterExtractor("A", Vector2Int.zero, null);

            Assert.AreEqual(0, registry.ClaimCount);
            Assert.IsFalse(registry.IsWorking(null, "A"));
            Assert.IsFalse(registry.IsWorking("CoalNode", null));
            Assert.IsFalse(registry.IsWorking("NoSuchNode", "A"));
            Assert.AreEqual(0, registry.CrewCount(null));
            Assert.IsFalse(registry.UnregisterExtractor("NeverRegistered"));
            CollectionAssert.IsEmpty(registry.CrewOf("NoSuchNode"));
        }
    }
}
