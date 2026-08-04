using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class ResourceNodeVisualStateTests
    {
        // Relative luminance, the same measure used to pick these values against the warm
        // plank floor. Asserting on it rather than on raw channels is what makes "a depleted
        // node looks spent" a testable claim instead of an opinion.
        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void Evaluate_FullNode_IsUntouched()
        {
            ResourceNodeVisual visual = ResourceNodeVisualState.Evaluate(40, 40);

            Assert.AreEqual(ResourceNodeVisualState.FullTint, visual.Tint);
            Assert.AreEqual(ResourceNodeVisualState.FullScale, visual.Scale, 0.0001f);
            Assert.IsFalse(visual.IsDepleted);
        }

        // An infinite node never drains, so draining it visually would be a lie.
        [Test]
        public void Evaluate_InfiniteNode_AlwaysReadsFull()
        {
            ResourceNodeVisual visual = ResourceNodeVisualState.Evaluate(ResourceNode.Infinite, 0);

            Assert.AreEqual(ResourceNodeVisualState.FullTint, visual.Tint);
            Assert.AreEqual(ResourceNodeVisualState.FullScale, visual.Scale, 0.0001f);
            Assert.IsFalse(visual.IsDepleted);
        }

        [Test]
        public void Evaluate_DepletedNode_IsMarkedAndVisiblyDimmerAndSmaller()
        {
            ResourceNodeVisual full = ResourceNodeVisualState.Evaluate(40, 40);
            ResourceNodeVisual empty = ResourceNodeVisualState.Evaluate(0, 40);

            Assert.IsTrue(empty.IsDepleted);
            Assert.Less(empty.Scale, full.Scale);
            // Well outside the band ordinary lighting moves a sprite through, so "empty"
            // can never be mistaken for "in shadow".
            Assert.Less(Luminance(empty.Tint), Luminance(full.Tint) * 0.5f);
        }

        [Test]
        public void Evaluate_DrainsMonotonically()
        {
            float previousLuminance = float.MaxValue;
            float previousScale = float.MaxValue;

            for (int remaining = 40; remaining >= 0; remaining--)
            {
                ResourceNodeVisual visual = ResourceNodeVisualState.Evaluate(remaining, 40);
                Assert.LessOrEqual(Luminance(visual.Tint), previousLuminance + 0.0001f);
                Assert.LessOrEqual(visual.Scale, previousScale + 0.0001f);
                previousLuminance = Luminance(visual.Tint);
                previousScale = visual.Scale;
            }
        }

        [Test]
        public void Evaluate_NeverShrinksToNothing()
        {
            Assert.Greater(ResourceNodeVisualState.Evaluate(0, 40).Scale, 0.5f);
        }

        [Test]
        public void Evaluate_ZeroPeak_DoesNotDivideByZero()
        {
            ResourceNodeVisual visual = ResourceNodeVisualState.Evaluate(5, 0);

            Assert.AreEqual(ResourceNodeVisualState.FullScale, visual.Scale, 0.0001f);
            Assert.IsFalse(visual.IsDepleted);
        }

        [Test]
        public void DescribeRemaining_DistinguishesInfiniteFromDepletedFromCounted()
        {
            Assert.AreEqual("unlimited", ResourceNodeVisualState.DescribeRemaining(ResourceNode.Infinite));
            Assert.AreEqual("depleted", ResourceNodeVisualState.DescribeRemaining(0));
            Assert.AreEqual("12 left", ResourceNodeVisualState.DescribeRemaining(12));
        }

        [Test]
        public void IsRunningLow_OnlyForFiniteNodesNearTheEnd()
        {
            Assert.IsFalse(ResourceNodeVisualState.IsRunningLow(ResourceNode.Infinite, 40));
            Assert.IsFalse(ResourceNodeVisualState.IsRunningLow(0, 40));
            Assert.IsFalse(ResourceNodeVisualState.IsRunningLow(30, 40));
            Assert.IsTrue(ResourceNodeVisualState.IsRunningLow(8, 40));
        }
    }
}
