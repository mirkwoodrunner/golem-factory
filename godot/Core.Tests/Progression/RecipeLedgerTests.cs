using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// What the Artificer's Ledger says about a recipe (docs/cozy-automation-design.md §4a). The
    /// chart lit nodes up and never said what any of them cost.
    /// </summary>
    public class RecipeLedgerTests
    {
        private const float TicksPerSecond = 10f;

        private static RecipeDefinition Recipe(
            (string type, int qty)[] inputs, string output, int outputQty, int durationTicks,
            string byproduct = null, int byproductQty = 1)
        {
            var recipe = new RecipeDefinition();
            foreach ((string type, int qty) in inputs)
            {
                recipe.inputs.Add(new RecipeIngredient(type, qty));
            }

            recipe.outputItemType = output;
            recipe.outputQuantity = outputQty;
            recipe.durationTicks = durationTicks;
            recipe.byproductItemType = byproduct;
            recipe.byproductQuantity = byproductQty;
            return recipe;
        }

        // R4 Iron Smelting, the recipe the whole Slag economy hangs off.
        private static RecipeDefinition IronSmelting() => Recipe(
            new[] { (ItemType.Scrap, 2), (ItemType.Coke, 1) },
            ItemType.IronPlate, 2, 24, ItemType.Slag, 1);

        [Test]
        public void TheRatioNamesEveryInputAndBothOutputs()
        {
            Assert.AreEqual(
                "2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag",
                RecipeLedger.FormatRatio(IronSmelting()));
        }

        [Test]
        public void ItemNamesAreHumanised()
        {
            // The Ledger says "Iron Plate" where the asset says "IronPlate".
            RecipeDefinition wire = Recipe(
                new[] { (ItemType.CopperIngot, 1) }, ItemType.CopperWire, 3, 18);

            Assert.AreEqual("1 Copper Ingot -> 3 Copper Wire", RecipeLedger.FormatRatio(wire));
        }

        [Test]
        public void ARecipeWithNoByproductSaysNothingAboutOne()
        {
            RecipeDefinition coking = Recipe(new[] { (ItemType.Coal, 1) }, ItemType.Coke, 1, 12);

            Assert.AreEqual("1 Coal -> 1 Coke", RecipeLedger.FormatRatio(coking));
            Assert.AreEqual("", RecipeLedger.FormatByproductRatio(coking));
        }

        [Test]
        public void TheByproductRatioIsWhatSizesASlagHeap()
        {
            // "1 Slag per 2 Iron Plate" is the sentence a player sizes disposal by;
            // "byproductQuantity: 1" is not.
            Assert.AreEqual("1 Slag per 2 Iron Plate", RecipeLedger.FormatByproductRatio(IronSmelting()));
        }

        [Test]
        public void TheByproductRatioIsReducedToLowestTerms()
        {
            RecipeDefinition lumpy = Recipe(
                new[] { (ItemType.Scrap, 6) }, ItemType.IronPlate, 6, 24, ItemType.Slag, 3);

            Assert.AreEqual("1 Slag per 2 Iron Plate", RecipeLedger.FormatByproductRatio(lumpy));
        }

        [Test]
        public void TheCycleLineCarriesTicksAndAPerMinuteRate()
        {
            // 2 Iron Plate per 24 ticks at 10 ticks/second = 50 per minute.
            Assert.AreEqual(
                "24 ticks  ·  50.0 Iron Plate/min at 1x",
                RecipeLedger.FormatCycle(IronSmelting(), TicksPerSecond));
        }

        [Test]
        public void ASingleTickRecipeSaysTickNotTicks()
        {
            RecipeDefinition instant = Recipe(new[] { (ItemType.Coal, 1) }, ItemType.Coke, 1, 1);

            StringAssert.StartsWith("1 tick  ·  ", RecipeLedger.FormatCycle(instant, TicksPerSecond));
        }

        [Test]
        public void AZeroDurationIsUnknown_NeverInfinity()
        {
            // A readout that printed infinity would be reporting an authoring bug as a throughput.
            Assert.AreEqual(-1f, RecipeLedger.RatePerMinute(2, 0, TicksPerSecond));
            Assert.AreEqual(-1f, RecipeLedger.RatePerMinute(2, -5, TicksPerSecond));
            Assert.AreEqual(-1f, RecipeLedger.RatePerMinute(2, 24, 0f));

            RecipeDefinition broken = Recipe(new[] { (ItemType.Coal, 1) }, ItemType.Coke, 1, 0);
            StringAssert.EndsWith(RecipeLedger.UnknownRate, RecipeLedger.FormatCycle(broken, TicksPerSecond));
        }

        [Test]
        public void AnUnmeasuredLineOmitsItsLiveRateRatherThanPrintingZero()
        {
            // "now: 0.0/min" for a line the player has not built yet would make an unstarted
            // branch look broken -- the same misdirection StallDiagnostics exists to prevent.
            Assert.AreEqual("", RecipeLedger.FormatLiveRate(measured: false, ratePerMinute: 0f));
            Assert.AreEqual("now: 3.2/min", RecipeLedger.FormatLiveRate(measured: true, ratePerMinute: 3.2f));
        }

        [Test]
        public void AMalformedRecipeShowsItsAuthoringProblemAndNothingElse()
        {
            // IsWellFormed(out problem) has always produced this string and nothing has ever
            // shown it to anybody. Printing a ratio derived from bad data beside it would dress
            // the fault up as a fact.
            var noOutput = new RecipeDefinition();
            noOutput.inputs.Add(new RecipeIngredient(ItemType.Scrap, 1));
            noOutput.outputItemType = "";

            string described = RecipeLedger.Describe(noOutput, TicksPerSecond);

            StringAssert.StartsWith("malformed: ", described);
            StringAssert.DoesNotContain("->", described);
        }

        [Test]
        public void DescribeStacksTheLinesItHas()
        {
            string described = RecipeLedger.Describe(
                IronSmelting(), TicksPerSecond, measured: true, livePerMinute: 12.5f);

            string[] lines = described.Split('\n');
            Assert.AreEqual(4, lines.Length, described);
            Assert.AreEqual("2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag", lines[0]);
            Assert.AreEqual("byproduct: 1 Slag per 2 Iron Plate", lines[1]);
            Assert.AreEqual("24 ticks  ·  50.0 Iron Plate/min at 1x", lines[2]);
            Assert.AreEqual("now: 12.5/min", lines[3]);
        }

        [Test]
        public void DescribeSkipsTheLinesItDoesNotHave()
        {
            RecipeDefinition coking = Recipe(new[] { (ItemType.Coal, 1) }, ItemType.Coke, 1, 12);

            string[] lines = RecipeLedger.Describe(coking, TicksPerSecond).Split('\n');

            Assert.AreEqual(2, lines.Length, "no byproduct line, no live line");
        }

        [Test]
        public void ARecipeIsFoundByWhatItMakes_NotByWhatItEmits()
        {
            // Main output only. Slag is R4's BYPRODUCT and R3's input, and no recipe makes it on
            // purpose -- so returning R4 under a Slag heading would show an iron recipe to a
            // player asking about Slag.
            RecipeDefinition iron = IronSmelting();
            RecipeDefinition glass = Recipe(new[] { (ItemType.Slag, 1) }, ItemType.Glass, 1, 20);
            var roster = new List<RecipeDefinition> { iron, glass };

            Assert.AreSame(iron, RecipeLedger.FindByOutput(roster, ItemType.IronPlate));
            Assert.AreSame(glass, RecipeLedger.FindByOutput(roster, ItemType.Glass));
            Assert.IsNull(RecipeLedger.FindByOutput(roster, ItemType.Slag));
        }

        [Test]
        public void NothingThrowsOnNulls()
        {
            // The readout runs in a frame loop and must never be the thing that throws.
            Assert.AreEqual("", RecipeLedger.FormatRatio(null));
            Assert.AreEqual("", RecipeLedger.FormatByproductRatio(null));
            Assert.AreEqual("", RecipeLedger.FormatCycle(null, TicksPerSecond));
            Assert.AreEqual("", RecipeLedger.Describe(null, TicksPerSecond));
            Assert.AreEqual("no recipe", RecipeLedger.FormatProblem(null));
            Assert.IsNull(RecipeLedger.FindByOutput(null, ItemType.Scrap));
            Assert.IsNull(RecipeLedger.FindByOutput(new List<RecipeDefinition>(), null));
        }

        [Test]
        public void FormatBundle_NamesEveryGood_NotJustTheFirst()
        {
            // The Assembly Line panel shares this: it used to print only a bundle's Scrap
            // component, so a card costing 8 Scrap + 4 Coke advertised "8 Scrap", lit its
            // Claim button off a Scrap-only check, and then refused the sale.
            string text = RecipeLedger.FormatBundle(new List<RecipeIngredient>
            {
                new RecipeIngredient(ItemType.Scrap, 8),
                new RecipeIngredient(ItemType.Coke, 4)
            });

            Assert.AreEqual("8 Scrap + 4 Coke", text);
            Assert.AreEqual("nothing", RecipeLedger.FormatBundle(null));
            Assert.AreEqual("nothing", RecipeLedger.FormatBundle(new List<RecipeIngredient>()));
        }

        [Test]
        public void FormatBundle_SplitsCamelCaseItemIds()
        {
            Assert.AreEqual("5 Iron Plate", RecipeLedger.FormatBundle(
                new List<RecipeIngredient> { new RecipeIngredient(ItemType.IronPlate, 5) }));
        }

        [Test]
        public void TheArrowIsAscii()
        {
            // TMP's default LiberationSans SDF atlas has no U+2192, the constraint
            // StallDiagnostics and WorkbenchLoopLabels both record.
            string ratio = RecipeLedger.FormatRatio(IronSmelting());
            StringAssert.Contains("->", ratio);
            foreach (char c in ratio)
            {
                Assert.LessOrEqual((int)c, 0xFF, "non-Latin-1 glyph in \"" + ratio + "\"");
            }
        }
    }
}
