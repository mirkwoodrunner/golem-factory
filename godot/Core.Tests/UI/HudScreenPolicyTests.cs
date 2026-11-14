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

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, true, false, true)]
        [TestCase(true, false, true, true)]
        [TestCase(false, true, true, true)]
        [TestCase(true, true, true, true)]
        public void HasOverlap_IsTrueOnlyWhenTwoOrMoreScreensAreOpen(
            bool workbench, bool management, bool construction, bool expected)
        {
            Assert.AreEqual(expected, HudScreenPolicy.HasOverlap(workbench, management, construction));
        }
    }
}
