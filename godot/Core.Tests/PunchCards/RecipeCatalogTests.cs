using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The authored recipe set, checked against docs/progression-design.md §5.2 -- ON DISK,
    /// not against the authoring script's in-memory table.
    ///
    /// <para>
    /// That distinction is the point of the whole file. ProgressionAssetAuthoring is a tuning
    /// tool that will be re-run every time a number moves (§12 asks for exactly that, and names
    /// the Lens ratio and the boiler burn as the first two things to try). A bad authoring run
    /// -- a typo, a half-finished edit, an asset someone hand-edited in the Inspector -- must
    /// fail the suite rather than fail the player mid-game, so everything here loads the
    /// .asset files the game actually ships.
    /// </para>
    /// </summary>
    public class RecipeCatalogTests
    {
        private const string RecipeRoot = "Assets/_Project/ScriptableObjects/Recipes";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis";

        private static List<RecipeDefinition> LoadRecipes()
        {
            var recipes = new List<RecipeDefinition>();
            recipes.AddRange(AuthoredData.All<RecipeDefinition>());

            return recipes;
        }

        private static List<ChassisDefinition> LoadChassis()
        {
            var chassis = new List<ChassisDefinition>();
            chassis.AddRange(AuthoredData.All<ChassisDefinition>());

            return chassis;
        }

        /// <summary>
        /// §5.2's own table, restated independently of the authoring script's copy so the two
        /// have to agree: recipe id -> (input type count, the chassis slot count it needs).
        /// <c>slots = inputs + 2</c> is §2 Consequence 1, the design's only tier gate.
        /// </summary>
        private static readonly Dictionary<string, (int Inputs, int MinSlots)> ExpectedShape =
            new Dictionary<string, (int, int)>
            {
                { "R1_Coking", (1, 3) },
                { "R2_ScrapReclamation", (1, 3) },
                { "R3_Glassmaking", (1, 3) },
                { "R8_GearCutting", (1, 3) },
                { "R19_WireDrawing", (1, 3) },
                { "R4_IronSmelting", (2, 4) },
                { "R5_CopperSmelting", (2, 4) },
                { "R6_ZincSmelting", (2, 4) },
                { "R7_BrassAlloying", (2, 4) },
                { "R9_CasingPress", (2, 4) },
                { "R10_LensGrinding", (2, 4) },
                { "R11_MainspringWinding", (2, 4) },
                { "R12_AetherContainment", (2, 4) },
                { "R13_MechanismAssembly", (3, 5) },
                { "R15_FrameSection", (3, 5) },
                { "R16_GreatCog", (3, 5) },
                { "R18_AetherConduit", (3, 5) },
                { "R14_Regulator", (4, 6) },
                { "R17_ChronometerCore", (4, 6) },
            };

        [Test]
        public void EveryAuthoredRecipeIsWellFormed()
        {
            foreach (RecipeDefinition recipe in LoadRecipes())
            {
                string problem;
                Assert.IsTrue(recipe.IsWellFormed(out problem),
                    recipe.name + " is malformed: " + problem);
            }
        }

        // §5.2's row count. The design's prose says "20 crafting + 5 extraction = 25 recipes"
        // (repeated in §12), but the table itself lists R1..R19 with no gaps, and its own ladder
        // tally -- Presser 5 + Hauler 8 + Overclocker 4 + Zeppelin 2 -- sums to 19. Nineteen is
        // what is authored; the prose is off by one. Pinned here so a future pass that "fixes"
        // the count by inventing a twentieth recipe has to come and argue with this test.
        [Test]
        public void TheRecipeSetIsExactlyTheOneSection5Point2Lists()
        {
            List<RecipeDefinition> recipes = LoadRecipes();

            // Deliberately NOT compared against ProgressionAssetAuthoring's own count. That
            // would be a tautology of exactly the kind this file's header argues against: edit
            // the authoring script to emit eighteen recipes, re-run it, and both sides move
            // together while the assertion keeps passing. ExpectedShape below is transcribed
            // from §5.2 by hand, so it is the only party here that can disagree with the disk.
            // (It also keeps the test assembly from having to reference GolemFactory.Editor,
            // which is authoring tooling, not something the shipped assets depend on.)
            Assert.AreEqual(ExpectedShape.Count, recipes.Count,
                "recipe count on disk does not match section 5.2's table");

            var seen = new HashSet<string>();
            foreach (RecipeDefinition recipe in recipes)
            {
                Assert.IsTrue(ExpectedShape.ContainsKey(recipe.name),
                    recipe.name + " is not a recipe section 5.2 lists");
                Assert.IsTrue(seen.Add(recipe.name), recipe.name + " is authored twice");
            }
        }

        // The tier gate is physical: a chassis with N slots runs a program of N cards, and
        // every processing program is N x Haul + Assemble + Push. So a recipe's input count
        // alone decides its minimum chassis, and no recipe may need more than the Zeppelin's 6.
        [Test]
        public void EachRecipeImpliesTheMinimumChassisSection5Point2Names()
        {
            foreach (RecipeDefinition recipe in LoadRecipes())
            {
                (int inputs, int minSlots) = ExpectedShape[recipe.name];

                Assert.AreEqual(inputs, recipe.inputs.Count,
                    recipe.name + " has the wrong number of input types");
                Assert.AreEqual(minSlots, recipe.inputs.Count + 2,
                    recipe.name + " implies a chassis section 5.2 does not name");
                Assert.LessOrEqual(recipe.inputs.Count, RecipeDefinition.MaxInputs,
                    recipe.name + " needs more slots than the largest chassis in the roster");
            }
        }

        // §5.2: "exactly one optional byproduct with its own quantity (R4 only)". The Slag is
        // the whole §5.3(c) disposal economy -- a second byproduct anywhere would be a second,
        // uncosted disposal obligation nobody has audited.
        [Test]
        public void OnlyIronSmeltingHasAByproduct()
        {
            foreach (RecipeDefinition recipe in LoadRecipes())
            {
                if (recipe.name == "R4_IronSmelting")
                {
                    Assert.IsTrue(recipe.HasByproduct);
                    Assert.AreEqual(ItemType.Slag, recipe.byproductItemType);
                    Assert.AreEqual(1, recipe.byproductQuantity);
                    Assert.AreEqual(2, recipe.outputQuantity, "R4 makes 2 Iron Plate");
                }
                else
                {
                    Assert.IsFalse(recipe.HasByproduct, recipe.name + " should have no byproduct");
                }
            }
        }

        /// <summary>
        /// §12 rubric point 1, "No dead-end resources -- PASS", turned from a claim the design
        /// makes about itself into a test.
        ///
        /// <para>
        /// Every Tier 0-4 item must have at least one consumer among the authored recipes or
        /// the five chassis cost bundles. The four Tier-5 goods are terminal BY DESIGN: the
        /// Clock Tower (§1.6, unbuilt) is their sink and the win condition, so they are excluded
        /// rather than exempted quietly.
        /// </para>
        ///
        /// <para>
        /// This is the check that catches the failure mode `Aether` was actually in before this
        /// pass -- an item in the tree with nothing downstream of it -- and it is cheap to keep
        /// permanently because a tuning pass that removes the last consumer of a good will trip
        /// it immediately.
        /// </para>
        /// </summary>
        [Test]
        public void NoDeadEndItems()
        {
            var terminal = new HashSet<string>
            {
                ItemType.FrameSection, ItemType.GreatCog,
                ItemType.AetherConduit, ItemType.ChronometerCore,
            };

            var consumed = new HashSet<string>();
            foreach (RecipeDefinition recipe in LoadRecipes())
            {
                foreach (RecipeIngredient input in recipe.inputs)
                {
                    consumed.Add(input.itemType);
                }
            }

            foreach (ChassisDefinition chassis in LoadChassis())
            {
                foreach (RecipeIngredient entry in chassis.cost)
                {
                    consumed.Add(entry.itemType);
                }
            }

            foreach (string itemType in AllItemTypes())
            {
                if (terminal.Contains(itemType))
                {
                    continue;
                }

                Assert.IsTrue(consumed.Contains(itemType),
                    itemType + " is a dead end -- no recipe and no chassis cost consumes it");
            }
        }

        // The counterpart check: every non-raw item must be MAKEABLE. A good with a consumer and
        // no producer is the same soft-lock from the other direction -- a recipe nobody can ever
        // satisfy, stalling MissingItem forever.
        [Test]
        public void EveryNonRawItemHasAProducer()
        {
            var raw = new HashSet<string>
            {
                ItemType.Scrap, ItemType.Coal, ItemType.CopperOre,
                ItemType.ZincOre, ItemType.Aether,
            };

            var produced = new HashSet<string>();
            foreach (RecipeDefinition recipe in LoadRecipes())
            {
                produced.Add(recipe.outputItemType);
                if (recipe.HasByproduct)
                {
                    produced.Add(recipe.byproductItemType);
                }
            }

            foreach (string itemType in AllItemTypes())
            {
                if (raw.Contains(itemType))
                {
                    continue;
                }

                Assert.IsTrue(produced.Contains(itemType),
                    itemType + " has no recipe that produces it");
            }
        }

        // §5.1's 24-item roster. Restated here rather than reflected out of ItemType so that
        // deleting a constant cannot silently shrink what the two audits above check.
        private static string[] AllItemTypes() => new[]
        {
            ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether,
            ItemType.Coke, ItemType.IronPlate, ItemType.Slag, ItemType.Glass,
            ItemType.CopperIngot, ItemType.ZincIngot, ItemType.Brass, ItemType.CopperWire,
            ItemType.Gear, ItemType.Casing, ItemType.Lens, ItemType.Mainspring, ItemType.AetherCell,
            ItemType.Mechanism, ItemType.Regulator,
            ItemType.FrameSection, ItemType.GreatCog, ItemType.AetherConduit, ItemType.ChronometerCore,
        };

        [Test]
        public void TheRosterIsExactlyTwentyFourItems()
        {
            Assert.AreEqual(24, new HashSet<string>(AllItemTypes()).Count,
                "section 5.1 lists 24 distinct item types");
        }
    }
}
