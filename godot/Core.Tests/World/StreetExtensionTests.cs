using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §13.1: the market street runs wider than the building it passes, so the world is a T
    /// rather than a rectangle. The Game Director chose this over a second stall row to keep the
    /// front-row extractor clearance and let factories run parallel vertical buses north.
    /// </summary>
    public class StreetExtensionTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);

        [Test]
        public void TheStreetIsWiderThanTheWorkshop()
        {
            Assert.Greater(FloorLayout.StreetHalfExtent, FloorLayout.HalfExtent);
        }

        [Test]
        public void NineStallsFitAtTheAuthoredFourCellPitch()
        {
            // The pitch and the origin are unchanged, which is what keeps the original five
            // stalls where they were -- their walk distances are on the do-not-tune list.
            var sites = new List<int>();
            for (int x = -16; x <= 16; x += 4)
            {
                sites.Add(x);
            }

            Assert.AreEqual(9, sites.Count, "§3.2 wants ~8 node sites; the pitch yields nine.");
            foreach (int x in sites)
            {
                Assert.IsTrue(
                    FloorLayout.IsInsideWorld(new Vector2Int(x, -16)),
                    "stall site " + x + " must be on the street");
            }

            // Two cells of margin at each end, so a stall is never flush against the boundary.
            Assert.AreEqual(2, FloorLayout.StreetHalfExtent - 16);
        }

        [Test]
        public void TheOriginalFiveStallsDidNotMove()
        {
            foreach (int x in new[] { -8, -4, 0, 4, 8 })
            {
                Assert.IsTrue(FloorLayout.IsInsideWorld(new Vector2Int(x, -16)));
            }
        }

        [Test]
        public void IsInsideWorld_IsTShaped_NotRectangular()
        {
            // Out on the street, past the building's flank: ground.
            Assert.IsTrue(FloorLayout.IsInsideWorld(new Vector2Int(16, -16)));

            // The same x, but level with the workshop: NOT ground. A rectangle would have said
            // yes here and let the player build in the empty space beside the shop front.
            Assert.IsFalse(FloorLayout.IsInsideWorld(new Vector2Int(16, 0)));
        }

        [Test]
        public void IsInsideWorld_AgreesWithGetWorldCells_OverTheWholeNeighbourhood()
        {
            var world = new HashSet<Vector2Int>(FloorLayout.GetWorldCells());

            for (int x = -FloorLayout.StreetHalfExtent - 3; x <= FloorLayout.StreetHalfExtent + 3; x++)
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
        public void ClampToFloor_LetsThePlayerOntoTheOuterStreet()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 wanted = converter.CellFractionToWorld(new Vector2(16f, -16f));

            Vector3 clamped = FloorLayout.ClampToFloor(wanted, converter);

            Assert.AreEqual(wanted.x, clamped.x, 0.001f);
            Assert.AreEqual(wanted.y, clamped.y, 0.001f);
        }

        [Test]
        public void ClampToFloor_StillStopsAtTheWorkshopWallInsideTheShop()
        {
            // The seam: the same x that is legal on the street is through a wall in the room.
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 wanted = converter.CellFractionToWorld(new Vector2(16f, 0f));

            Vector3 clamped = FloorLayout.ClampToFloor(wanted, converter);
            Vector2 cell = converter.WorldToCellFraction(clamped);

            Assert.AreEqual(FloorLayout.HalfExtent, cell.x, 0.001f);
        }

        [Test]
        public void ClampToFloor_OffTheSouthEndAtAnOuterX_StaysOnTheStreet()
        {
            var converter = new GridCoordinateConverter(CellSize);
            // One cell past the kerb, not twenty: from far enough south the town square (G10) is
            // the nearer ground, which is right for a teleport and never reached by walking.
            Vector3 wanted = converter.CellFractionToWorld(new Vector2(17f, FloorLayout.WorldMinY - 1f));

            Vector2 cell = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));

            Assert.AreEqual(FloorLayout.WorldMinY, cell.y, 0.001f);
            Assert.AreEqual(17f, cell.x, 0.001f);
        }

        [Test]
        public void WalkingNorthOnTheOuterStreet_HitsAWall_NotTheWorkshop()
        {
            // THE CLIP FOUND IN PLAY. Standing on the outer street beside the building and
            // walking north used to clamp Y first -- which put the player on a workshop row,
            // where the legal width is only the room's, so the X clamp then teleported them
            // sideways THROUGH the building's flank and into the shop.
            //
            // The shoulder has to behave like the wall it is drawn as: movement north stops.
            //
            // WALKED, NOT TELEPORTED -- one movement step from a legal spot on the outer street,
            // which is what the player actually did. A teleport to the middle of that empty space
            // is a different question, and "nearest legal point" rightly answers it with the room.
            var converter = new GridCoordinateConverter(CellSize);
            var onTheStreet = new Vector2(16f, -13f);

            for (int step = 0; step < 40; step++)
            {
                Vector3 wanted = converter.CellFractionToWorld(onTheStreet + new Vector2(0f, 0.15f));
                onTheStreet = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));

                Assert.AreEqual(16f, onTheStreet.x, 0.001f,
                    "must NOT be pulled sideways into the workshop, at step " + step);
                Assert.LessOrEqual(onTheStreet.y, -FloorLayout.HalfExtent - 1f + 0.001f,
                    "and must be held south of the shop front, at step " + step);
            }
        }

        [Test]
        public void WalkingSouthDownTheMiddle_CrossesIntoTheTownSquare_AndStopsAtItsKerb()
        {
            // G10: the square opens off the street. The street-to-square seam must not snag a
            // walking player, and the square's far kerb must stop them.
            var converter = new GridCoordinateConverter(CellSize);
            var at = new Vector2(0f, -16f);
            for (int step = 0; step < 200; step++)
            {
                Vector3 wanted = converter.CellFractionToWorld(at + new Vector2(0f, -0.15f));
                at = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));
                Assert.AreEqual(0f, at.x, 0.001f, "straight down the middle, at step " + step);
            }
            Assert.AreEqual(TownSquare.Bottom, at.y, 0.001f, "walked the whole square, to its kerb");
        }

        [Test]
        public void WalkingSouthBesideTheSquare_StopsAtTheStreetKerb()
        {
            // And past the square's flank there is still kerb: a player there is not pulled
            // sideways into the square.
            var converter = new GridCoordinateConverter(CellSize);
            float x = TownSquare.HalfExtent + 3f;
            var at = new Vector2(x, -16f);
            for (int step = 0; step < 100; step++)
            {
                Vector3 wanted = converter.CellFractionToWorld(at + new Vector2(0f, -0.15f));
                at = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));
                Assert.AreEqual(x, at.x, 0.001f, "never pulled sideways, at step " + step);
            }
            Assert.AreEqual(FloorLayout.WorldMinY, at.y, 0.001f, "held at the street's kerb");
        }

        [Test]
        public void TheWorkshopItselfIsStillFullyWalkable()
        {
            // The fix must not shrink the room: a player at the far corner of the shop floor
            // stays exactly where they are.
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 wanted = converter.CellFractionToWorld(
                new Vector2(FloorLayout.HalfExtent, FloorLayout.HalfExtent));

            Vector2 cell = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));

            Assert.AreEqual(FloorLayout.HalfExtent, cell.x, 0.001f);
            Assert.AreEqual(FloorLayout.HalfExtent, cell.y, 0.001f);
        }

        [Test]
        public void WalkingSouthOutOfTheShopFront_IsNotBlocked()
        {
            // The other half of the seam: the shop front is OPEN, so walking south out of the
            // room and onto the road has to be continuous. An earlier fix made the road's north
            // edge a hard line for every column, which would have penned the player indoors.
            var converter = new GridCoordinateConverter(CellSize);
            var position = new Vector2(0f, -11.5f);

            for (int step = 0; step < 20; step++)
            {
                Vector3 wanted = converter.CellFractionToWorld(position - new Vector2(0f, 0.15f));
                position = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));
            }

            Assert.Less(position.y, -FloorLayout.HalfExtent,
                "the player must be able to walk out onto the street");
        }

        [Test]
        public void TheStreetIsStillWalkableEndToEnd()
        {
            var converter = new GridCoordinateConverter(CellSize);
            foreach (float x in new[] { -17f, -13f, 0f, 13f, 17f })
            {
                Vector3 wanted = converter.CellFractionToWorld(new Vector2(x, -16f));
                Vector2 cell = converter.WorldToCellFraction(FloorLayout.ClampToFloor(wanted, converter));

                Assert.AreEqual(x, cell.x, 0.001f, "the whole road is walkable, at x = " + x);
                Assert.AreEqual(-16f, cell.y, 0.001f);
            }
        }

        [Test]
        public void SideWallAnchors_StepOutAtTheShopFront()
        {
            // North of the shop front the side wall is the building's own.
            Vector2 room = FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.East, 0);
            Assert.AreEqual(FloorLayout.HalfExtent + 0.5f, room.x, 0.001f);

            // South of it, it is the street's -- six cells further out.
            Vector2 street = FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.East, -16);
            Assert.AreEqual(FloorLayout.StreetHalfExtent + 0.5f, street.x, 0.001f);

            Vector2 west = FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.West, -16);
            Assert.AreEqual(-(FloorLayout.StreetHalfExtent + 0.5f), west.x, 0.001f);
        }

        [Test]
        public void ShoulderAnchors_CloseTheGapBesideTheShopFront()
        {
            // Without these, the ground north of the outer street has no boundary drawn and the
            // cobbles run off into background -- the "unfinished tilemap" read paving fixed once.
            var anchors = new List<Vector2>(FloorLayout.GetShoulderAnchors());
            int perSide = FloorLayout.StreetHalfExtent - FloorLayout.HalfExtent;

            Assert.AreEqual(perSide * 2, anchors.Count);
            foreach (Vector2 anchor in anchors)
            {
                Assert.AreEqual(-FloorLayout.HalfExtent - 0.5f, anchor.y, 0.001f,
                    "a shoulder sits on the workshop's south line");
                Assert.Greater(Mathf.Abs(anchor.x), FloorLayout.HalfExtent,
                    "and outside the building it flanks");
                Assert.LessOrEqual(Mathf.Abs(anchor.x), FloorLayout.StreetHalfExtent);
            }
        }

        [Test]
        public void TheKerbSpansTheStreet_NotTheWorkshop()
        {
            var indices = new List<int>(FloorLayout.GetStreetEdgeIndices());

            Assert.AreEqual(FloorLayout.StreetHalfExtent * 2 + 1, indices.Count);
            Assert.AreEqual(-FloorLayout.StreetHalfExtent, indices[0]);
            Assert.AreEqual(FloorLayout.StreetHalfExtent, indices[indices.Count - 1]);
        }

        [Test]
        public void TheWorkshopItselfIsUnchanged()
        {
            // §3.3 sizes the 44-golem Phase-5 factory against exactly 25x25. Widening the road
            // must not have touched the room.
            Assert.AreEqual(12, FloorLayout.HalfExtent);

            var floor = new HashSet<Vector2Int>(FloorLayout.GetFloorCells());
            Assert.AreEqual(25 * 25, floor.Count);
            Assert.IsFalse(floor.Contains(new Vector2Int(13, 0)));
        }
    }
}
