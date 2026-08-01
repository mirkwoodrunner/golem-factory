using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class FloorTileVariantTests
    {
        [Test]
        public void Select_IsDeterministic()
        {
            Assert.AreEqual(FloorTileVariant.Select(7, -3), FloorTileVariant.Select(7, -3));
            Assert.AreEqual(FloorTileVariant.Select(-11, 12), FloorTileVariant.Select(-11, 12));
        }

        // The half of the floor at negative cell coordinates is exactly where a sign-preserving
        // % would have produced a negative tile index and thrown on the tile lookup.
        [Test]
        public void Select_StaysInRangeAcrossTheWholeFloorIncludingNegativeCells()
        {
            foreach (Vector2Int cell in FloorLayout.GetFloorCells())
            {
                int index = FloorTileVariant.Select(cell.x, cell.y);

                Assert.GreaterOrEqual(index, 0);
                Assert.Less(index, FloorTileVariant.TileCount);
            }
        }

        [Test]
        public void Select_UsesEveryPlankVariantAcrossTheFloor()
        {
            var seen = new HashSet<int>();
            foreach (Vector2Int cell in FloorLayout.GetFloorCells())
            {
                seen.Add(FloorTileVariant.Select(cell.x, cell.y));
            }

            for (int i = 0; i < FloorTileVariant.PlankVariantCount; i++)
            {
                Assert.IsTrue(seen.Contains(i), "plank variant " + i + " never appears on the floor");
            }
        }

        // Accents are landmarks, not texture. If they ever creep above a few percent the floor
        // stops reading as a wooden workshop floor and starts reading as noise again.
        [Test]
        public void Select_KeepsAccentTilesRare()
        {
            int total = 0;
            int accents = 0;
            foreach (Vector2Int cell in FloorLayout.GetFloorCells())
            {
                total++;
                if (FloorTileVariant.IsAccent(FloorTileVariant.Select(cell.x, cell.y)))
                {
                    accents++;
                }
            }

            Assert.Greater(accents, 0, "the floor should have some accent tiles");
            Assert.Less(accents / (float)total, 0.12f, "accent tiles must stay sparse");
        }

        [Test]
        public void Select_DoesNotProduceLongRunsOfTheSameVariant()
        {
            // A hash that degenerates into stripes would tile visibly; assert no row of the
            // floor is a single repeated variant.
            for (int y = -FloorLayout.HalfExtent; y <= FloorLayout.HalfExtent; y++)
            {
                var seen = new HashSet<int>();
                for (int x = -FloorLayout.HalfExtent; x <= FloorLayout.HalfExtent; x++)
                {
                    seen.Add(FloorTileVariant.Select(x, y));
                }
                Assert.Greater(seen.Count, 1, "row " + y + " uses a single tile variant");
            }
        }

        [Test]
        public void IsAccent_ClassifiesPlanksAndAccentsApart()
        {
            for (int i = 0; i < FloorTileVariant.PlankVariantCount; i++)
            {
                Assert.IsFalse(FloorTileVariant.IsAccent(i));
            }
            Assert.IsTrue(FloorTileVariant.IsAccent(FloorTileVariant.PlateIndex));
            Assert.IsTrue(FloorTileVariant.IsAccent(FloorTileVariant.GrateIndex));
        }
    }
}
