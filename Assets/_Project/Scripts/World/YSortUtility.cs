using UnityEngine;

namespace GolemFactory.World
{
    // Pure math extracted from YSortSpriteRenderer so it's unit-testable without a scene,
    // mirroring GridCoordinateConverter's split between plain-C# math and the MonoBehaviour
    // that applies it. Isometric sprites need "further back" (larger world Y) to draw behind
    // "further forward" (smaller world Y) -- a plain Sorting Layer can't express that since it's
    // static per-object, so this converts world Y into a per-frame sortingOrder instead.
    public static class YSortUtility
    {
        private const int PrecisionMultiplier = 100;

        public static int ComputeSortingOrder(float worldY)
        {
            return Mathf.RoundToInt(-worldY * PrecisionMultiplier);
        }
    }
}
