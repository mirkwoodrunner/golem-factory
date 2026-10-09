using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// The town square south of the market street, and the Clock Tower's fixed site in it (G10,
    /// the user's call: "the clock tower is supposed to be the final project ... It's available
    /// from the start and the model looks like a grandfather clock which isn't appropriate").
    ///
    /// <para>
    /// <b>The tower is a landmark, not furniture.</b> It used to be a free placeable that went
    /// anywhere from minute one, so the game's endgame project sat in the build menu beside the
    /// depot. Now it has one site, in a public square the workshop's front door faces down the
    /// street, roped off until the factory has built a Zeppelin. The player walks out to it and
    /// feeds it from there; nothing about that needs a build-menu row.
    /// </para>
    ///
    /// <para>
    /// The square opens off the street's south side and is narrower than it, so the world is a
    /// cross: workshop, street, square. Everything here is cell arithmetic only; the Godot shell
    /// paints and walls it from these answers.
    /// </para>
    /// </summary>
    public static class TownSquare
    {
        /// <summary>Rows of square south of the street.</summary>
        public const int Depth = 10;

        /// <summary>Half-width, in cells: seventeen wide, opening off the street's middle.</summary>
        public const int HalfExtent = 8;

        /// <summary>The square's northern row, the one against the street.</summary>
        public const int Top = FloorLayout.WorldMinY - 1;

        /// <summary>The square's southern row.</summary>
        public const int Bottom = Top - Depth + 1;

        /// <summary>The tower's footprint is three cells square.</summary>
        public const int TowerSize = 3;

        /// <summary>The footprint's south-west cell. Centred on the square's middle.</summary>
        public static readonly Vector2Int TowerOrigin = new Vector2Int(-1, (Top + Bottom) / 2 - 1);

        /// <summary>The footprint's centre cell: where the building is drawn and saved from.</summary>
        public static Vector2Int TowerCentre => TowerOrigin + new Vector2Int(1, 1);

        public static IEnumerable<Vector2Int> Cells()
        {
            for (int y = Top; y >= Bottom; y--)
            {
                for (int x = -HalfExtent; x <= HalfExtent; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        public static bool Contains(Vector2Int cell) =>
            cell.y <= Top && cell.y >= Bottom && cell.x >= -HalfExtent && cell.x <= HalfExtent;

        public static IEnumerable<Vector2Int> TowerCells()
        {
            for (int y = 0; y < TowerSize; y++)
            {
                for (int x = 0; x < TowerSize; x++)
                {
                    yield return TowerOrigin + new Vector2Int(x, y);
                }
            }
        }

        public static bool IsTowerCell(Vector2Int cell) =>
            cell.x >= TowerOrigin.x && cell.x < TowerOrigin.x + TowerSize
            && cell.y >= TowerOrigin.y && cell.y < TowerOrigin.y + TowerSize;

        /// <summary>
        /// The ring of cells around the footprint: where the rope runs while the site is closed,
        /// and where a golem stands to deliver once it is open.
        /// </summary>
        public static IEnumerable<Vector2Int> TowerRing()
        {
            for (int y = -1; y <= TowerSize; y++)
            {
                for (int x = -1; x <= TowerSize; x++)
                {
                    if (x == -1 || y == -1 || x == TowerSize || y == TowerSize)
                    {
                        yield return TowerOrigin + new Vector2Int(x, y);
                    }
                }
            }
        }
    }
}
