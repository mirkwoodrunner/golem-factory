using GolemFactory.Compat;
using GolemFactory.Player;
using NUnit.Framework;

namespace GolemFactory.Tests.Player
{
    public class CameraRigRulesTests
    {
        [Test]
        public void FollowClosesTheGapExponentially()
        {
            Vector2 next = CameraRigRules.Follow(new Vector2(0f, 0f), new Vector2(10f, -10f), 0.1f);
            Assert.AreEqual(10f * (1f - (float)System.Math.Exp(-0.5)), next.x, 1e-4, "5/s for 0.1 s closes 39% of the gap");
            Assert.AreEqual(-next.x, next.y, 1e-4);
        }

        [Test]
        public void TwoHalfFramesEqualOneWholeFrame()
        {
            Vector2 half = CameraRigRules.Follow(Vector2.zero, new Vector2(10f, 0f), 0.05f);
            half = CameraRigRules.Follow(half, new Vector2(10f, 0f), 0.05f);
            Vector2 whole = CameraRigRules.Follow(Vector2.zero, new Vector2(10f, 0f), 0.1f);
            Assert.AreEqual(whole.x, half.x, 1e-4, "frame-rate independent");
        }

        [Test]
        public void ASlowFrameNeverOvershootsThePlayer()
        {
            Vector2 next = CameraRigRules.Follow(Vector2.zero, new Vector2(3f, 4f), 2f);
            Assert.AreEqual(3f, next.x, 1e-3);
            Assert.AreEqual(4f, next.y, 1e-3);
        }

        [Test]
        public void ZoomIsClampedBetweenThreeAndFifteen()
        {
            Assert.AreEqual(3f, CameraRigRules.Zoom(4f, 100f, 1f));
            Assert.AreEqual(15f, CameraRigRules.Zoom(14f, -100f, 1f));
            Assert.AreEqual(9.5f, CameraRigRules.Zoom(10f, 1f, 0.1f), 1e-4, "positive input zooms in");
            Assert.AreEqual(10f, CameraRigRules.Zoom(10f, 0f, 1f), "no input, no change");
        }
    }
}
