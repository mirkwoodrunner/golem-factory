using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Tests.Data;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// Where belt cargo is drawn (G10, from playtest: "the scrap on the belt looks weird"), and
    /// how fast the Sandbox's belts run ("it moves down the belt too fast").
    /// </summary>
    public class BeltCargoPathTests
    {
        private static BeltNetwork Network()
        {
            var network = new BeltNetwork();
            network.Configure(new ConveyorSystem(), null, 4);
            return network;
        }

        private static void AreClose(Vector2 expected, Vector2 actual, string message = "")
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4, message + " x");
            Assert.AreEqual(expected.y, actual.y, 1e-4, message + " y");
        }

        [Test]
        public void AStraightRun_DrawsEdgeToEdge_AsBefore()
        {
            BeltNetwork network = Network();
            network.TryPlace(new Vector2Int(0, 0), Facing.East, out PlacedBelt feed);
            network.TryPlace(new Vector2Int(1, 0), Facing.East, out PlacedBelt belt);
            network.TryPlace(new Vector2Int(2, 0), Facing.East, out _);

            Vector2 entry = BeltCargoPath.Entry(network, belt.Cell, belt.Segment, Facing.East, false);
            Vector2 exit = BeltCargoPath.Exit(network, belt.Cell, belt.Segment, Facing.East, false);

            AreClose(new Vector2(-0.5f, 0f), BeltCargoPath.Point(entry, exit, 0f), "enters at the back");
            AreClose(new Vector2(0f, 0f), BeltCargoPath.Point(entry, exit, 0.5f), "crosses the centre");
            AreClose(new Vector2(0.5f, 0f), BeltCargoPath.Point(entry, exit, 1f), "leaves at the front");
            AreClose(new Vector2(-0.25f, 0f), BeltCargoPath.Point(entry, exit, 0.25f), "one straight line");
        }

        [Test]
        public void ACorner_TakesItsCargoInFromTheSideThatFeedsIt()
        {
            // Fed from the south, pointing east: Unity drew this cargo arriving from the WEST.
            BeltNetwork network = Network();
            network.TryPlace(new Vector2Int(0, -1), Facing.North, out _);
            network.TryPlace(new Vector2Int(0, 0), Facing.East, out PlacedBelt corner);
            network.TryPlace(new Vector2Int(1, 0), Facing.East, out _);

            AreClose(new Vector2(0f, -0.5f), BeltCargoPath.Entry(network, corner.Cell, corner.Segment, Facing.East, false));
            AreClose(new Vector2(0.5f, 0f), BeltCargoPath.Exit(network, corner.Cell, corner.Segment, Facing.East, false));
        }

        [Test]
        public void ADeadEnd_StopsItsFrontItemShortOfTheEdge()
        {
            BeltNetwork network = Network();
            network.TryPlace(new Vector2Int(0, 0), Facing.North, out PlacedBelt end);

            Vector2 exit = BeltCargoPath.Exit(network, end.Cell, end.Segment, Facing.North, false);

            AreClose(new Vector2(0f, BeltCargoPath.DeadEndStop), exit);
            Assert.Less(BeltCargoPath.DeadEndStop + 0.25f, 0.5f + 1e-4f, "a half-cell item stays on the tile");
        }

        [Test]
        public void ASplitter_SendsItsFrontItemTowardTheBranchItWillTakeNext()
        {
            BeltNetwork network = Network();
            network.TryPlace(new Vector2Int(-1, 0), Facing.East, out _);
            network.TryPlace(new Vector2Int(0, 0), Facing.North, true, out PlacedBelt splitter);
            network.TryPlace(new Vector2Int(0, 1), Facing.North, out PlacedBelt north);
            network.TryPlace(new Vector2Int(1, 0), Facing.East, out PlacedBelt east);

            AreClose(new Vector2(-0.5f, 0f), BeltCargoPath.Entry(network, splitter.Cell, splitter.Segment, Facing.North, true), "fed from the west");

            Vector2 first = BeltCargoPath.Exit(network, splitter.Cell, splitter.Segment, Facing.North, true);
            BeltSegment firstBranch = splitter.Segment.Outputs[splitter.Segment.HandoffCursor];
            AreClose(firstBranch == north.Segment ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0f), first);

            splitter.Segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            for (int i = 0; i < splitter.Segment.Length; i++)
            {
                splitter.Segment.Advance(1f);
            }
            Assert.IsTrue(splitter.Segment.TryHandOff(), "precondition: handed off");

            Vector2 second = BeltCargoPath.Exit(network, splitter.Cell, splitter.Segment, Facing.North, true);
            Assert.AreNotEqual(first.x, second.x, "the round-robin moved on, and the drawn exit with it");
        }

        [Test]
        public void TheSandboxBeltsRunAtOneCellASecond()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            Assert.AreEqual(1f, world.Setup.beltCellsPerSecond, "sandbox.json");

            world.Belts.TryPlace(new Vector2Int(0, 0), Facing.East, out PlacedBelt belt);
            belt.Segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            int ticksPerCell = 0;
            while (belt.Segment.Items[0].Progress < belt.Segment.Length)
            {
                world.Conveyor.Tick(++ticksPerCell);
            }

            Assert.AreEqual(world.Clock.TicksPerSecond, ticksPerCell, 1e-4, "one cell takes one second of ticks");
        }
    }
}
