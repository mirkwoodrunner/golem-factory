using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Where a freshly constructed golem stands. Matters because facing is routing now: a golem
    // spawned inside the station would read the station's neighbours as its source/target
    // tiles rather than the ones the player was aiming at.
    public class GolemSpawnPlacementTests
    {
        private static System.Func<Vector2Int, bool> Occupied(params Vector2Int[] cells)
        {
            var set = new HashSet<Vector2Int>(cells);
            return set.Contains;
        }

        [Test]
        public void TheGolemStepsOutOntoTheTileTheStationFaces()
        {
            Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(
                new Vector2Int(3, 3), Facing.East, Occupied());

            Assert.AreEqual(new Vector2Int(4, 3), spawn);
        }

        [Test]
        public void TheStationsOwnCellIsNeverChosen()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Vector2Int station = new Vector2Int(2, 2);
                Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(station, facing, Occupied());
                Assert.AreNotEqual(station, spawn, $"{facing} spawned the golem inside the station");
            }
        }

        [Test]
        public void ABlockedFrontTile_FallsBackToTheNextNeighbourClockwise()
        {
            // Facing North at (0,0): front is (0,1). Blocked, so the next clockwise facing
            // (East) gives (1,0).
            Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(
                Vector2Int.zero, Facing.North, Occupied(new Vector2Int(0, 1)));

            Assert.AreEqual(new Vector2Int(1, 0), spawn);
        }

        [Test]
        public void ItWalksPastSeveralBlockedNeighbours()
        {
            Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(
                Vector2Int.zero, Facing.North,
                Occupied(new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1)));

            Assert.AreEqual(new Vector2Int(-1, 0), spawn);
        }

        [Test]
        public void AFullyBoxedInStation_StillSpawnsInFront()
        {
            // A golem overlapping a neighbour is recoverable; refusing to spawn after already
            // charging the chassis cost is not.
            Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(
                Vector2Int.zero, Facing.North,
                Occupied(new Vector2Int(0, 1), new Vector2Int(1, 0),
                         new Vector2Int(0, -1), new Vector2Int(-1, 0)));

            Assert.AreEqual(new Vector2Int(0, 1), spawn);
        }

        [Test]
        public void NoOccupancyOracle_StillResolvesToTheFrontTile()
        {
            // A station with no GridMap wired must not throw.
            Vector2Int spawn = GolemSpawnPlacement.ResolveSpawnCell(new Vector2Int(5, 5), Facing.South, null);

            Assert.AreEqual(new Vector2Int(5, 4), spawn);
        }
    }
}
