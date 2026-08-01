namespace GolemFactory.World
{
    // Which of the workshop floor's tile sprites a given cell gets. Pure integer math with no
    // UnityEngine types at all -- same math/MonoBehaviour split as GridCoordinateConverter and
    // YSortUtility, so it is EditMode-testable without a scene and, more importantly, so the
    // pattern is *deterministic*: SandboxFloorGenerator can repaint the floor any number of
    // times and always produce the identical layout, instead of a random scatter that changes
    // on every regeneration and shows up as scene-diff churn.
    //
    // The plank variants exist only to break up tiling; the two accent tiles (a riveted brass
    // inspection plate and a steam grate) are deliberately RARE -- they are landmarks the eye
    // navigates by, and at any higher density they stop reading as details and start reading
    // as noise, which is what the old grey stone floor was.
    public static class FloorTileVariant
    {
        public const int PlankVariantCount = 4;
        public const int PlateIndex = PlankVariantCount;
        public const int GrateIndex = PlankVariantCount + 1;
        public const int TileCount = PlankVariantCount + 2;

        // Coprime-ish moduli, so the two accent lattices interleave rather than landing on top
        // of each other in a repeating clump. Grate is tested first, so where the lattices do
        // coincide the rarer tile wins.
        private const int GrateModulus = 43;
        private const int PlateModulus = 29;

        public static int Select(int cellX, int cellY)
        {
            if (Mod(cellX + cellY * 3, GrateModulus) == 0)
            {
                return GrateIndex;
            }
            if (Mod(cellX * 5 + cellY * 3, PlateModulus) == 0)
            {
                return PlateIndex;
            }
            return Mod(Hash(cellX, cellY), PlankVariantCount);
        }

        public static bool IsAccent(int tileIndex)
        {
            return tileIndex >= PlankVariantCount;
        }

        private static int Hash(int x, int y)
        {
            unchecked
            {
                int h = (x * 73856093) ^ (y * 19349663);
                h ^= h >> 13;
                h *= 1274126177;
                return h ^ (h >> 16);
            }
        }

        // C#'s % keeps the sign of the dividend, so a plain `x % m` would return negatives for
        // the half of the floor at negative cell coordinates and index out of the tile array.
        private static int Mod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }
    }
}
