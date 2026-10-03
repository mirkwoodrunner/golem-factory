using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §5.1's tier grouping, transcribed from <c>ItemType</c>'s own comments into something code
    /// can ask. Pinned against the constants themselves by reflection rather than against a second
    /// hand-written list -- a 25th good must be given a tier or this suite fails, which is the
    /// whole point of the file existing.
    /// </summary>
    public class ItemTiersTests
    {
        private static IEnumerable<string> AllItemTypeConstants()
        {
            FieldInfo[] fields = typeof(ItemType).GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            foreach (FieldInfo field in fields)
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                {
                    yield return (string)field.GetRawConstantValue();
                }
            }
        }

        [Test]
        public void EveryItemTypeConstantHasATier()
        {
            foreach (string itemType in AllItemTypeConstants())
            {
                Assert.AreNotEqual(
                    ItemTiers.Unknown, ItemTiers.TierOf(itemType),
                    itemType + " has no tier -- add it to ItemTiers.Order and its tier's size");
            }
        }

        [Test]
        public void TheCanonicalOrderIsExactlyTheRoster()
        {
            var constants = new HashSet<string>(AllItemTypeConstants());
            var ordered = new HashSet<string>(ItemTiers.CanonicalOrder);

            // Both directions: a good missing from the order would sort to the end silently, and
            // a good in the order that ItemType no longer defines is a stale entry nothing else
            // would catch.
            CollectionAssert.AreEquivalent(constants, ordered);
        }

        [Test]
        public void TheOrderIsGroupedByTier_ShallowestFirst()
        {
            int previous = 0;
            foreach (string itemType in ItemTiers.CanonicalOrder)
            {
                int tier = ItemTiers.TierOf(itemType);
                Assert.GreaterOrEqual(tier, previous, itemType + " breaks the tier grouping");
                previous = tier;
            }

            Assert.AreEqual(ItemTiers.MaxTier, previous, "the order should end in the deepest tier");
        }

        [Test]
        public void TheFiveTierZeroGoodsAreTheOnesTheMarketSells()
        {
            // The stalls on the market street, from the playtest script's A3: Scrap, Coal,
            // Copper, Zinc, Aether. If a good moved out of tier 0 the street would be selling
            // something the tier table calls processed.
            Assert.AreEqual(0, ItemTiers.TierOf(ItemType.Scrap));
            Assert.AreEqual(0, ItemTiers.TierOf(ItemType.Coal));
            Assert.AreEqual(0, ItemTiers.TierOf(ItemType.CopperOre));
            Assert.AreEqual(0, ItemTiers.TierOf(ItemType.ZincOre));
            Assert.AreEqual(0, ItemTiers.TierOf(ItemType.Aether));
        }

        [Test]
        public void TheFourTierFiveGoodsAreTheClockTowersS()
        {
            Assert.AreEqual(ItemTiers.MaxTier, ItemTiers.TierOf(ItemType.FrameSection));
            Assert.AreEqual(ItemTiers.MaxTier, ItemTiers.TierOf(ItemType.GreatCog));
            Assert.AreEqual(ItemTiers.MaxTier, ItemTiers.TierOf(ItemType.AetherConduit));
            Assert.AreEqual(ItemTiers.MaxTier, ItemTiers.TierOf(ItemType.ChronometerCore));
        }

        [Test]
        public void SlagIsTierOne_WhichIsWhatPricesTheRecycler()
        {
            // Load-bearing for docs/cozy-automation-design.md §4b's arithmetic: Slag at tier 1
            // is 2 points, so 2 Slag buys 1 Scrap for 1 Coke -- exactly half the Slag Heap's
            // disposal rate. Move Slag's tier and that trade silently changes.
            Assert.AreEqual(1, ItemTiers.TierOf(ItemType.Slag));
        }

        [Test]
        public void AnUnknownGoodIsRefused_NotDefaulted()
        {
            Assert.AreEqual(ItemTiers.Unknown, ItemTiers.TierOf("Unobtanium"));
            Assert.AreEqual(ItemTiers.Unknown, ItemTiers.TierOf(""));
            Assert.AreEqual(ItemTiers.Unknown, ItemTiers.TierOf(null));
            Assert.IsFalse(ItemTiers.IsKnown("Unobtanium"));
        }

        [Test]
        public void AnUnknownGoodSortsToTheEnd_NotTheFront()
        {
            Assert.AreEqual(int.MaxValue, ItemTiers.CanonicalPosition("Unobtanium"));
            Assert.Less(ItemTiers.CanonicalPosition(ItemType.Scrap),
                        ItemTiers.CanonicalPosition(ItemType.IronPlate));
        }

        [Test]
        public void DisplayNameSplitsCamelCase()
        {
            Assert.AreEqual("Iron Plate", ItemTiers.DisplayName(ItemType.IronPlate));
            Assert.AreEqual("Copper Ore", ItemTiers.DisplayName(ItemType.CopperOre));
            Assert.AreEqual("Aether Cell", ItemTiers.DisplayName(ItemType.AetherCell));
            Assert.AreEqual("Chronometer Core", ItemTiers.DisplayName(ItemType.ChronometerCore));
        }

        [Test]
        public void DisplayNameLeavesSingleWordsAlone()
        {
            Assert.AreEqual("Scrap", ItemTiers.DisplayName(ItemType.Scrap));
            Assert.AreEqual("Coke", ItemTiers.DisplayName(ItemType.Coke));
            Assert.AreEqual("Slag", ItemTiers.DisplayName(ItemType.Slag));
            Assert.AreEqual("", ItemTiers.DisplayName(null));
        }
    }
}
