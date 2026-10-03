using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // The wording of the "+1 Coke" confirmation. Pure, so no scene and no Play mode -- the same
    // split as InteractionTargetingTests, and the reason the three places goods reach the
    // player's stockpile can now be held to one phrasing instead of three hand-written ones.
    public class YieldPopupTextTests
    {
        [Test]
        public void Gain_ReadsAsTheHarvestConfirmationAlwaysDid()
        {
            Assert.AreEqual("+1 Coke", YieldPopupText.Gain(ItemType.Coke, 1));
            Assert.AreEqual("+3 Coke", YieldPopupText.Gain(ItemType.Coke, 3));
        }

        [Test]
        public void Gain_NamesTheGoodTheWayAPersonReadsIt()
        {
            // The drift this replaced: the harvest line printed the raw buffer key, so copper
            // ore read "+1 CopperOre" while every panel in the game called it Copper Ore.
            Assert.AreEqual("+1 Copper Ore", YieldPopupText.Gain(ItemType.CopperOre, 1));
            Assert.AreEqual("+2 Iron Plate", YieldPopupText.Gain(ItemType.IronPlate, 2));
        }

        [Test]
        public void Gain_IsEmptyForNothingGained()
        {
            // Callers hand the result straight to FloatingPopup.Spawn, so an empty string is a
            // caption that says nothing -- which is the right amount of noise for a gain that
            // did not happen. A visible "+0" would read as a bug.
            Assert.IsEmpty(YieldPopupText.Gain(ItemType.Coke, 0));
            Assert.IsEmpty(YieldPopupText.Gain(ItemType.Coke, -2));
            Assert.IsEmpty(YieldPopupText.Gain(null, 1));
            Assert.IsEmpty(YieldPopupText.Gain("", 1));
        }

        [Test]
        public void Gain_IsRenderableByTheProjectAtlas()
        {
            // The TMP rule: nothing above U+00FF, or it draws as a missing-glyph box. Every
            // item id in the game goes through here, so walk the whole roster.
            foreach (string itemType in ItemTiers.CanonicalOrder)
            {
                foreach (char c in YieldPopupText.Gain(itemType, 1))
                {
                    Assert.Less((int)c, 0x100, "unrenderable glyph in the popup for " + itemType);
                }
            }
        }
    }
}
