using System.Collections.Generic;
using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>The compact build menu's words and keys (G10, from playtest).</summary>
    public class BuildMenuLabelsTests
    {
        [Test]
        public void EveryPlaceable_HasAReadableName_AHotkey_AndADescription()
        {
            List<PlaceableEntry> placeables = PlaceableCatalogTests.LoadReal(AuthoredData.Load()).ToList();
            Assert.LessOrEqual(placeables.Count, BuildMenuLabels.RowKeys.Length, "every row gets a key");

            for (int i = 0; i < placeables.Count; i++)
            {
                string key = placeables[i].Key;
                string name = BuildMenuLabels.NameFor(key);
                StringAssert.DoesNotContain("Prefab", name, key);
                Assert.IsFalse(name.Contains("  "), key);
                Assert.IsNotEmpty(BuildMenuLabels.DescriptionFor(key), key + " has no description");
                Assert.IsNotEmpty(BuildMenuLabels.KeyFor(i), key);
            }
            CollectionAssert.AllItemsAreUnique(BuildMenuLabels.RowKeys.Append(BuildMenuLabels.DemolishKey));
        }

        [TestCase("GolemConstructionStationPrefab", "Golem Station")]
        [TestCase("BeltSplitterPrefab", "Belt Splitter")]
        [TestCase("SteamPipePrefab", "Steam Pipe")]
        [TestCase("HandCrankBenchPrefab", "Hand-Crank Bench")]
        [TestCase("DepotPrefab", "Depot")]
        public void Names(string key, string expected) => Assert.AreEqual(expected, BuildMenuLabels.NameFor(key));

        [Test]
        public void Keys_RunOneToNineThenZero()
        {
            Assert.AreEqual("1", BuildMenuLabels.KeyFor(0));
            Assert.AreEqual("9", BuildMenuLabels.KeyFor(8));
            Assert.AreEqual("0", BuildMenuLabels.KeyFor(9));
            Assert.AreEqual("-", BuildMenuLabels.KeyFor(10));
            Assert.AreEqual("", BuildMenuLabels.KeyFor(12));
        }

        [Test]
        public void TheCostLine_NamesTheShortfall()
        {
            var cost = new[] { new RecipeIngredient(ItemType.Scrap, 30), new RecipeIngredient(ItemType.IronPlate, 10) };
            var stock = new Dictionary<string, int> { [ItemType.Scrap] = 40, [ItemType.IronPlate] = 6 };

            string line = BuildMenuLabels.CostLine(cost, i => stock.TryGetValue(i, out int q) ? q : 0, out bool affordable);

            Assert.IsFalse(affordable);
            Assert.AreEqual("30 Scrap  +  10 Iron Plate  ·  short 4 Iron Plate", line);

            stock[ItemType.IronPlate] = 10;
            Assert.AreEqual("30 Scrap  +  10 Iron Plate", BuildMenuLabels.CostLine(cost, i => stock[i], out affordable));
            Assert.IsTrue(affordable);
            Assert.AreEqual("Free", BuildMenuLabels.CostLine(new RecipeIngredient[0], i => 0, out _));
        }
    }
}
