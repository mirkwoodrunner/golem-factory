using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // The bench readout's pure formatting. Cranking with no feedback was the worst-feeling thing
    // in the opening: a 96-tick craft with no bar, no statement of what is being made and no
    // warning that you cannot afford it, so the player holds a key for ten seconds and finds out
    // at the end. The bench is the slowest thing in the game by design, which makes it the thing
    // that least tolerates having no readout.
    public class HandCrankReadoutTests
    {
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }

            _assets.Clear();
        }

        private RecipeDefinition MakeRecipe(
            string assetName, string output, int outputQuantity, params (string type, int qty)[] inputs)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.name = assetName;
            recipe.inputs = new List<RecipeIngredient>();
            foreach ((string type, int qty) in inputs)
            {
                recipe.inputs.Add(new RecipeIngredient(type, qty));
            }

            recipe.outputItemType = output;
            recipe.outputQuantity = outputQuantity;
            _assets.Add(recipe);
            return recipe;
        }

        [Test]
        public void RecipeLabel_KeepsTheRNumberAndSpacesTheName()
        {
            RecipeDefinition r2 = MakeRecipe("R2_ScrapReclamation", "IronPlate", 1, ("Scrap", 1));

            // The R-number is how the design doc, the recipe assets and the appendage cards all
            // refer to a recipe, so a player reading any of those can match them up.
            Assert.AreEqual("R2 · Scrap Reclamation", HandCrankReadout.RecipeLabel(r2));
            Assert.AreEqual("nothing", HandCrankReadout.RecipeLabel(null));
        }

        [Test]
        public void ConversionLabel_SpacesItemIdsAndIncludesAByproduct()
        {
            RecipeDefinition r8 = MakeRecipe("R8_GearCutting", "Gear", 1, ("IronPlate", 2));
            Assert.AreEqual("2 Iron Plate → 1 Gear", HandCrankReadout.ConversionLabel(r8));

            RecipeDefinition r4 = MakeRecipe("R4_IronSmelting", "IronPlate", 2, ("Scrap", 2), ("Coke", 1));
            r4.byproductItemType = "Slag";
            r4.byproductQuantity = 1;
            Assert.AreEqual("2 Scrap + 1 Coke → 2 Iron Plate + 1 Slag",
                HandCrankReadout.ConversionLabel(r4));
        }

        [Test]
        public void ProgressPercent_ClampsAndSurvivesAZeroDuration()
        {
            Assert.AreEqual(0, HandCrankReadout.ProgressPercent(0, 96));
            Assert.AreEqual(50, HandCrankReadout.ProgressPercent(48, 96));
            Assert.AreEqual(100, HandCrankReadout.ProgressPercent(96, 96));
            Assert.AreEqual(100, HandCrankReadout.ProgressPercent(200, 96), "clamped, never over 100");
            Assert.AreEqual(0, HandCrankReadout.ProgressPercent(5, 0), "no divide by zero");
        }

        [Test]
        public void RemainingSeconds_RoundsUpSoTheCountdownNeverSitsOnZeroWhileTurning()
        {
            // A countdown reading 0s while the handle is still moving reads as a hang.
            Assert.AreEqual(10, HandCrankReadout.RemainingSeconds(0, 96, 10f));
            Assert.AreEqual(1, HandCrankReadout.RemainingSeconds(95, 96, 10f));
            Assert.AreEqual(0, HandCrankReadout.RemainingSeconds(96, 96, 10f));
            Assert.AreEqual(0, HandCrankReadout.RemainingSeconds(0, 0, 10f));
        }

        [Test]
        public void ProgressBar_FillsProportionally()
        {
            Assert.AreEqual("[░░░░░░░░░░]", HandCrankReadout.ProgressBar(0));
            Assert.AreEqual("[█████░░░░░]", HandCrankReadout.ProgressBar(50));
            Assert.AreEqual("[██████████]", HandCrankReadout.ProgressBar(100));
            Assert.AreEqual("[██████████]", HandCrankReadout.ProgressBar(150), "clamped");
        }

        [Test]
        public void ShortfallLabel_NamesTheFirstShortIngredientAndTheAmount()
        {
            RecipeDefinition r8 = MakeRecipe("R8_GearCutting", "Gear", 1, ("IronPlate", 2));

            Assert.AreEqual("need 2 more Iron Plate", HandCrankReadout.ShortfallLabel(r8, _ => 0));
            Assert.AreEqual("need 1 more Iron Plate", HandCrankReadout.ShortfallLabel(r8, _ => 1));
            Assert.AreEqual(string.Empty, HandCrankReadout.ShortfallLabel(r8, _ => 5));
        }

        [Test]
        public void Format_AwayFromABenchRendersNothingAtAll()
        {
            Assert.AreEqual(string.Empty, HandCrankReadout.Format(HandCrankReading.Away()));
        }

        [Test]
        public void Format_AffordableAndCrankingShowsTheBarAndACountdown()
        {
            var reading = new HandCrankReading(
                hasBench: true, recipeLabel: "R2 · Scrap Reclamation",
                conversionLabel: "1 Scrap → 1 Iron Plate", progressPercent: 50,
                remainingSeconds: 5, canAfford: true, shortfallLabel: "", isCranking: true,
                recipeCount: 5);

            string text = HandCrankReadout.Format(reading);
            StringAssert.Contains("R2 · Scrap Reclamation", text);
            StringAssert.Contains("1 Scrap → 1 Iron Plate", text);
            StringAssert.Contains("[█████░░░░░] 50%", text);
            StringAssert.Contains("5s left", text);
            StringAssert.Contains("hold [E] to crank", text);
        }

        [Test]
        public void Format_UnaffordableSaysSoInsteadOfTheHint()
        {
            var reading = new HandCrankReading(
                hasBench: true, recipeLabel: "R8 · Gear Cutting",
                conversionLabel: "2 Iron Plate → 1 Gear", progressPercent: 0,
                remainingSeconds: 0, canAfford: false, shortfallLabel: "need 2 more Iron Plate",
                isCranking: false, recipeCount: 5);

            string text = HandCrankReadout.Format(reading);
            StringAssert.Contains("need 2 more Iron Plate", text);
            StringAssert.DoesNotContain("hold [E] to crank", text,
                "telling the player to crank something they cannot afford is the bug this fixes");
        }

        [Test]
        public void Format_NotCrankingOmitsTheCountdownButKeepsTheBar()
        {
            var reading = new HandCrankReading(
                hasBench: true, recipeLabel: "R1 · Coking", conversionLabel: "1 Coal → 1 Coke",
                progressPercent: 0, remainingSeconds: 5, canAfford: true, shortfallLabel: "",
                isCranking: false, recipeCount: 5);

            string text = HandCrankReadout.Format(reading);
            StringAssert.Contains("[░░░░░░░░░░] 0%", text);
            StringAssert.DoesNotContain("s left", text, "nothing is counting down while nobody is turning it");
        }

        [Test]
        public void Format_ABenchWithNothingCrankableSaysSo()
        {
            var reading = new HandCrankReading(
                hasBench: true, recipeLabel: "nothing", conversionLabel: "", progressPercent: 0,
                remainingSeconds: 0, canAfford: false, shortfallLabel: "", isCranking: false,
                recipeCount: 0);

            StringAssert.Contains("nothing here can be made by hand", HandCrankReadout.Format(reading));
        }
    }
}
