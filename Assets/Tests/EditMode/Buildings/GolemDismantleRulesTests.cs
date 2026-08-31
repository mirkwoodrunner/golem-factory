using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// What a dismantle pays back. The refund is a golem's chassis cost PLUS its cargo, because
    /// removal in this game is never punitive -- and the golem a player most wants rid of is
    /// usually the one stalled holding something.
    /// </summary>
    public class GolemDismantleRulesTests
    {
        private static List<RecipeIngredient> Bundle(params (string type, int qty)[] lines)
        {
            var bundle = new List<RecipeIngredient>();
            foreach ((string type, int qty) in lines)
            {
                bundle.Add(new RecipeIngredient(type, qty));
            }
            return bundle;
        }

        private static int QuantityOf(IReadOnlyList<RecipeIngredient> bundle, string itemType)
        {
            for (int i = 0; i < bundle.Count; i++)
            {
                if (bundle[i].itemType == itemType)
                {
                    return bundle[i].quantity;
                }
            }
            return 0;
        }

        [Test]
        public void ComposeRefund_PaysBackTheChassisAndBothStocks()
        {
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(
                Bundle((ItemType.Scrap, 12), (ItemType.Brass, 4)),
                Bundle((ItemType.Coal, 8)),
                Bundle((ItemType.Coke, 3)));

            Assert.AreEqual(4, refund.Count);
            Assert.AreEqual(12, QuantityOf(refund, ItemType.Scrap));
            Assert.AreEqual(4, QuantityOf(refund, ItemType.Brass));
            Assert.AreEqual(8, QuantityOf(refund, ItemType.Coal));
            Assert.AreEqual(3, QuantityOf(refund, ItemType.Coke));
        }

        [Test]
        public void ComposeRefund_SumsAGoodThatAppearsInMoreThanOneBundle()
        {
            // A Scavenger costs Scrap and can be carrying Scrap in both stocks. Three lines of
            // the same good would make the caller ask the buffer for room three times and print
            // three "+n Scrap" fragments in one popup.
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(
                Bundle((ItemType.Scrap, 12)),
                Bundle((ItemType.Scrap, 5)),
                Bundle((ItemType.Scrap, 2)));

            Assert.AreEqual(1, refund.Count, "one good must produce exactly one refund line");
            Assert.AreEqual(19, refund[0].quantity);
        }

        [Test]
        public void ComposeRefund_NoChassisCost_StillPaysBackTheCargo()
        {
            // A scene-authored golem: nobody bought the chassis, so refunding it would mint
            // goods out of the scenery. What it is CARRYING is real either way.
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(
                null,
                Bundle((ItemType.Coal, 8)),
                Bundle());

            Assert.AreEqual(1, refund.Count);
            Assert.AreEqual(ItemType.Coal, refund[0].itemType);
            Assert.AreEqual(8, refund[0].quantity);
        }

        [Test]
        public void ComposeRefund_EmptyEverything_IsAnEmptyBundleNotNull()
        {
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(null, null, null);

            Assert.IsNotNull(refund);
            Assert.AreEqual(0, refund.Count);
        }

        [Test]
        public void ComposeRefund_DropsEmptyAndZeroLines()
        {
            // A zero line would render as "+0 Coal" in the popup and would make the room check
            // ask the buffer about a good the payout never touches.
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(
                Bundle((ItemType.Scrap, 12), (ItemType.Brass, 0)),
                Bundle((null, 5)),
                Bundle(("", 3)));

            Assert.AreEqual(1, refund.Count);
            Assert.AreEqual(ItemType.Scrap, refund[0].itemType);
        }

        [Test]
        public void ComposeRefund_KeepsFirstSeenOrder()
        {
            // Order is first-seen so two identically-loaded golems quote the same refund string.
            // GolemInventory.Stock enumerates by TypesInOrder for the same reason.
            List<RecipeIngredient> refund = GolemDismantleRules.ComposeRefund(
                Bundle((ItemType.Brass, 4)),
                Bundle((ItemType.Coal, 8)),
                Bundle((ItemType.Brass, 1), (ItemType.Coke, 3)));

            Assert.AreEqual(ItemType.Brass, refund[0].itemType);
            Assert.AreEqual(5, refund[0].quantity, "the later Brass merges into the first line");
            Assert.AreEqual(ItemType.Coal, refund[1].itemType);
            Assert.AreEqual(ItemType.Coke, refund[2].itemType);
        }
    }
}
