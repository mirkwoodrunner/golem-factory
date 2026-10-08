using GolemFactory.Compat;
using GolemFactory.Player;
using NUnit.Framework;

namespace GolemFactory.Tests.Player
{
    public class CameraRigRulesTests
    {
        [Test]
        public void FollowClosesTheGapByLerpSpeedTimesDelta()
        {
            Vector2 next = CameraRigRules.Follow(new Vector2(0f, 0f), new Vector2(10f, -10f), 0.1f);
            Assert.AreEqual(5f, next.x, 1e-4, "5/s for 0.1 s closes half the gap");
            Assert.AreEqual(-5f, next.y, 1e-4);
        }

        [Test]
        public void ASlowFrameNeverOvershootsThePlayer()
        {
            Vector2 next = CameraRigRules.Follow(Vector2.zero, new Vector2(3f, 4f), 2f);
            Assert.AreEqual(3f, next.x, 1e-4);
            Assert.AreEqual(4f, next.y, 1e-4);
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
