using UnityEngine;

namespace GolemFactory.World
{
    // Which golem's source/target tiles are currently lit. Pure, engine-free-enough to call
    // from a test without a scene, following the same split as InteractionTargeting
    // .SelectNearest (which this deliberately mirrors rather than extends -- that one picks
    // across three interactable kinds for the [E] prompt, this one only ranks golems).
    public static class RoutingFocus
    {
        public const int None = -1;

        /// <summary>
        /// Index of the nearest position to <paramref name="origin"/> within
        /// <paramref name="maxDistance"/>, or <see cref="None"/>.
        /// </summary>
        /// <remarks>
        /// Bounded by maxDistance on purpose. Highlighting whichever golem happens to be
        /// closest no matter how far away it is would leave two tiles glowing on the far side
        /// of the map with no visible cause -- worse than showing nothing, because the player
        /// cannot tell what it refers to.
        /// </remarks>
        public static int SelectNearestIndex(Vector3 origin, Vector3[] positions, float maxDistance)
        {
            if (positions == null)
            {
                return None;
            }

            int best = None;
            float bestSqr = maxDistance * maxDistance;
            for (int i = 0; i < positions.Length; i++)
            {
                float sqr = (positions[i] - origin).sqrMagnitude;
                // Strictly less-than, so ties keep the earlier index and the highlight does not
                // flicker between two equidistant golems on successive frames.
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }
    }
}
