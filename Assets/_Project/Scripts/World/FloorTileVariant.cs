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

        // ACCENTS ARE DRAWN FROM THE HASH, NOT FROM A CONGRUENCE.
        //
        // This used to test `x + 3y = 0 (mod 43)` and `5x + 3y = 0 (mod 29)`, with a comment
        // claiming that coprime-ish moduli made the two lattices interleave rather than clump.
        // That reasoning was about the wrong thing. A linear congruence over the integer plane
        // always defines a perfect 2-D lattice; coprimality changes WHICH lattice, never WHETHER
        // it is one. Measured over the real 25x25 room, every plate sat at a constant offset of
        // (-4,-3) from its nearest neighbour and every grate at (3,-1) -- so the accents marched
        // in clean diagonal chains across the floor.
        //
        // That is the one place isometric came back. The geometry is square now, but landmarks
        // stepping diagonally is exactly the tell the projection switch set out to remove.
        //
        // Thresholding the hash has no lattice by construction. Different bit-slices of the same
        // hash are used for the two draws so they are independent of each other and of the plank
        // choice, which reads the low bits.
        private const int GrateRarity = 300;
        private const int PlateRarity = 180;

        public static int Select(int cellX, int cellY)
        {
            int h = Hash(cellX, cellY);
            if (Mod(h >> 8, GrateRarity) == 0)
            {
                return GrateIndex;
            }
            if (Mod(h >> 16, PlateRarity) == 0)
            {
                return PlateIndex;
            }
            return Mod(h, PlankVariantCount);
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
