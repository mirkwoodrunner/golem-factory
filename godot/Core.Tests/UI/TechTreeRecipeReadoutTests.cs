using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's TechTreeRecipeReadoutTests, ported onto Core's <see cref="TechTreeReadout"/> (G8).
    /// TheReadoutIsParentedOutsideTheChart_SoARebuildDoesNotDestroyIt and
    /// EveryNodePlaqueIsClickable are about the drawn chart and are checked by the `management`
    /// scenario against the Godot Ledger.
    /// </summary>
    public class TechTreeRecipeReadoutTests
    {
        private static RecipeDefinition IronSmelting()
        {
            var recipe = new RecipeDefinition();
            recipe.inputs.Add(new RecipeIngredient(ItemType.Scrap, 2));
            recipe.inputs.Add(new RecipeIngredient(ItemType.Coke, 1));
            recipe.outputItemType = ItemType.IronPlate;
            recipe.outputQuantity = 2;
            recipe.byproductItemType = ItemType.Slag;
            recipe.byproductQuantity = 1;
            recipe.durationTicks = 24;
            // THE ASSET NAME IS THE IDENTITY the readout resolves by: a blank name would silently
            // test the catalog's fallback line instead of the recipe lookup.
            recipe.name = "R4_IronSmelting";
            return recipe;
        }

        private static TechTreeReadout Readout() => new TechTreeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);

        [Test]
        public void ClickingARecipeNodeShowsItsRatioAndByproduct()
        {
            TechTreeReadout readout = Readout();
            readout.Select("r4.ironsmelting");

            string body = readout.Body;
            StringAssert.Contains("2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag", body);
            StringAssert.Contains("1 Slag per 2 Iron Plate", body);
            StringAssert.Contains("24 ticks", body);
        }

        [Test]
        public void ANonRecipeNodeKeepsTheCatalogsOwnLine()
        {
            TechTreeReadout readout = Readout();
            readout.Select("bldg.slagheap");
            Assert.IsTrue(TechTreeCatalog.TryGetNode("bldg.slagheap", out TechTreeNode node));
            Assert.AreEqual(node.Detail, readout.Body);
        }

        [Test]
        public void AnUnmeasuredLineShowsNoLiveRate()
        {
            TechTreeReadout readout = Readout();
            readout.Select("r4.ironsmelting");
            StringAssert.DoesNotContain("now:", readout.Body);
        }

        [Test]
        public void NothingIsSelectedUntilSomethingIsClicked()
        {
            TechTreeReadout readout = Readout();
            Assert.IsNull(readout.SelectedNodeId);
            Assert.AreEqual("", readout.Body);
        }
    }
}
