using NUnit.Framework;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    // Found in playtest: closing the golem construction panel also tried to build a Depot.
    // BuildModeController subscribed to the Click action directly, and an InputAction fires
    // wherever the cursor is -- so every click on every piece of UI also attempted a world
    // placement at whatever cell the menu happened to be covering. Clicking a row in the build
    // menu did it too, which meant *selecting* a placeable immediately tried to place one.
    //
    // It hid for a long time because every placeable was free and placement was silent; restoring
    // the costs is what made it start refusing out loud.
    public class BuildClickPolicyTests
    {
        [Test]
        public void AClickOverUiNeverReachesTheWorld()
        {
            Assert.IsFalse(BuildClickPolicy.ShouldPlace(hasPlaceableInHand: true, pointerOverUi: true),
                "the click belonged to the panel that was under the cursor");
        }

        [Test]
        public void AClickOverOpenWorldWithSomethingInHandPlaces()
        {
            Assert.IsTrue(BuildClickPolicy.ShouldPlace(hasPlaceableInHand: true, pointerOverUi: false));
        }

        [Test]
        public void NothingInHandNeverPlaces()
        {
            Assert.IsFalse(BuildClickPolicy.ShouldPlace(hasPlaceableInHand: false, pointerOverUi: false));
            Assert.IsFalse(BuildClickPolicy.ShouldPlace(hasPlaceableInHand: false, pointerOverUi: true));
        }
    }
}
