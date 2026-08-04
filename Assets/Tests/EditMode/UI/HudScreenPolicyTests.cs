using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    public class HudScreenPolicyTests
    {
        [Test]
        public void ShouldShowWorldHud_OnlyWhenEveryFullScreenIsClosed()
        {
            Assert.IsTrue(HudScreenPolicy.ShouldShowWorldHud(false, false, false));
            Assert.IsFalse(HudScreenPolicy.ShouldShowWorldHud(true, false, false));
            Assert.IsFalse(HudScreenPolicy.ShouldShowWorldHud(false, true, false));
            Assert.IsFalse(HudScreenPolicy.ShouldShowWorldHud(false, false, true));
            Assert.IsFalse(HudScreenPolicy.ShouldShowWorldHud(true, true, true));
        }

        [Test]
        public void HasOverlap_IsFalseForZeroOrOneOpenScreen()
        {
            Assert.IsFalse(HudScreenPolicy.HasOverlap(false, false, false));
            Assert.IsFalse(HudScreenPolicy.HasOverlap(true, false, false));
            Assert.IsFalse(HudScreenPolicy.HasOverlap(false, true, false));
            Assert.IsFalse(HudScreenPolicy.HasOverlap(false, false, true));
        }

        [Test]
        public void HasOverlap_IsTrueForEveryPairAndTheTriple()
        {
            Assert.IsTrue(HudScreenPolicy.HasOverlap(true, true, false));
            Assert.IsTrue(HudScreenPolicy.HasOverlap(true, false, true));
            Assert.IsTrue(HudScreenPolicy.HasOverlap(false, true, true));
            Assert.IsTrue(HudScreenPolicy.HasOverlap(true, true, true));
        }
    }
}
