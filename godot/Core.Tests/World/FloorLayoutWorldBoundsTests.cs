using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// <see cref="FloorLayout.IsInsideWorld"/> -- the predicate form of GetWorldCells, and what
    /// bounds building since §3.3. Held to GetWorldCells itself rather than to a restatement of
    /// its arithmetic, so the two cannot drift.
    /// </summary>
    public class FloorLayoutWorldBoundsTests
    {
        [Test]
        public void IsInsideWorld_AgreesWithGetWorldCells_OverTheWholeNeighbourhood()
        {
            var world = new System.Collections.Generic.HashSet<Vector2Int>(
                FloorLayout.GetWorldCells());

            for (int x = -FloorLayout.HalfExtent - 3; x <= FloorLayout.HalfExtent + 3; x++)
            {
                for (int y = FloorLayout.WorldMinY - 3; y <= FloorLayout.WorldMaxY + 3; y++)
                {
                    var cell = new Vector2Int(x, y);
                    Assert.AreEqual(world.Contains(cell), FloorLayout.IsInsideWorld(cell),
                        "disagreed at " + cell);
                }
            }
        }

        [Test]
        public void IsInsideWorld_CoversTheStreetThatIsInsideWorkshopDoesNot()
        {
            // The market row. A workshop-only bound would forbid building here, which would make
            // the five traders unreachable by belt or depot -- the reason the world, not the
            // room, is what bounds placement.
            var stall = new Vector2Int(0, -16);

            Assert.IsFalse(FloorLayout.IsInsideWorkshop(stall));
            Assert.IsTrue(FloorLayout.IsInsideWorld(stall));
        }

        [Test]
        public void IsInsideWorld_RejectsPastTheKerbAndPastTheWalls()
        {
            // Past the street's kerb beside the town square, and past the square's own (G10).
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(TownSquare.HalfExtent + 1, FloorLayout.WorldMinY - 1)));
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(0, TownSquare.Bottom - 1)));
            Assert.IsTrue(FloorLayout.IsInsideWorld(new Vector2Int(0, FloorLayout.WorldMinY - 1)), "the square opens off the street");
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(0, FloorLayout.WorldMaxY + 1)));
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(FloorLayout.HalfExtent + 1, 0)));
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(-FloorLayout.HalfExtent - 1, 0)));
        }
    }
}
