using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Where a freshly constructed golem stands, as a pure function of the station's own
    // placement -- the same "extract the math into an engine-free static a test can call
    // without a scene" split as FacingUtility / GridCoordinateConverter / YSortUtility.
    //
    // This exists because "spawn the golem on the station's cell" is not actually viable once
    // facing is load-bearing: the station occupies that cell, so the golem would be standing
    // inside the building, and its own source/target tiles would be the station's neighbours
    // rather than the ones the player was aiming at. Emitting the golem onto the tile the
    // station faces makes placement legible -- the player points the station where they want
    // the golem to work, and the golem appears there facing the same way.
    public static class GolemSpawnPlacement
    {
        /// <summary>
        /// Cell a station at <paramref name="stationCell"/> facing <paramref name="stationFacing"/>
        /// should emit its golem onto: the tile directly in front. If that tile is taken, walks
        /// clockwise through the remaining three neighbours and takes the first free one.
        /// Falls back to the tile in front when the station is fully boxed in -- a golem that
        /// overlaps a neighbour is recoverable (the player can rotate or rebuild), whereas
        /// refusing to spawn after charging the chassis cost is not.
        /// </summary>
        public static Vector2Int ResolveSpawnCell(
            Vector2Int stationCell, Facing stationFacing, System.Func<Vector2Int, bool> isOccupied)
        {
            Vector2Int preferred = FacingUtility.TargetCell(stationCell, stationFacing);
            if (isOccupied == null)
            {
                return preferred;
            }

            Facing candidate = stationFacing;
            for (int i = 0; i < 4; i++)
            {
                Vector2Int cell = FacingUtility.TargetCell(stationCell, candidate);
                if (!isOccupied(cell))
                {
                    return cell;
                }

                candidate = FacingUtility.RotateClockwise(candidate);
            }

            return preferred;
        }
    }
}
