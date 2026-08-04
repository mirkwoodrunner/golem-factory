using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class RoutingFocusTests
    {
        [Test]
        public void PicksTheNearestPositionInRange()
        {
            var positions = new[]
            {
                new Vector3(5f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(3f, 0f, 0f)
            };

            Assert.AreEqual(1, RoutingFocus.SelectNearestIndex(Vector3.zero, positions, 10f));
        }

        [Test]
        public void NothingWithinRange_SelectsNone()
        {
            // Bounded on purpose: two tiles glowing on the far side of the map with no visible
            // cause is worse than showing nothing.
            var positions = new[] { new Vector3(50f, 0f, 0f) };

            Assert.AreEqual(RoutingFocus.None, RoutingFocus.SelectNearestIndex(Vector3.zero, positions, 3.5f));
        }

        [Test]
        public void AnEmptyOrNullSet_SelectsNone()
        {
            Assert.AreEqual(RoutingFocus.None, RoutingFocus.SelectNearestIndex(Vector3.zero, new Vector3[0], 5f));
            Assert.AreEqual(RoutingFocus.None, RoutingFocus.SelectNearestIndex(Vector3.zero, null, 5f));
        }

        [Test]
        public void InfinitePlaceholdersAreNeverSelected()
        {
            // Destroyed golems are parked at +infinity by the caller rather than reallocating
            // the array, so they must lose to anything real -- and to nothing at all.
            var infinity = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var positions = new[] { infinity, new Vector3(2f, 0f, 0f) };

            Assert.AreEqual(1, RoutingFocus.SelectNearestIndex(Vector3.zero, positions, 5f));
            Assert.AreEqual(RoutingFocus.None,
                RoutingFocus.SelectNearestIndex(Vector3.zero, new[] { infinity }, 5f));
        }

        [Test]
        public void TiesKeepTheEarlierIndex()
        {
            // Stable rather than arbitrary, so the highlight cannot flicker between two
            // equidistant golems on successive frames.
            var positions = new[] { new Vector3(2f, 0f, 0f), new Vector3(-2f, 0f, 0f) };

            Assert.AreEqual(0, RoutingFocus.SelectNearestIndex(Vector3.zero, positions, 5f));
        }
    }
}
