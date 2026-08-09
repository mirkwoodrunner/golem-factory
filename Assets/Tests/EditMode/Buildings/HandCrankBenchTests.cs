using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // The Hand-Crank Bench (docs/progression-design.md §11 item 7, §9 Phase 1). This is the thing
    // that breaks the opening's circularity -- a 2-slot Scavenger cannot produce, the 3-slot
    // Presser costs 20 Iron Plate + 10 Gear, and only hand-cranking can make those -- so the
    // properties pinned here are load-bearing on the game being playable at all.
    public class HandCrankBenchTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                Object.DestroyImmediate(go);
            }

            _spawned.Clear();

            foreach (Object asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }

            _assets.Clear();
        }

        private RecipeDefinition MakeRecipe(
            string output, int outputQuantity, int durationTicks, params (string type, int qty)[] inputs)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.inputs = new List<RecipeIngredient>();
            foreach ((string type, int qty) in inputs)
            {
                recipe.inputs.Add(new RecipeIngredient(type, qty));
            }

            recipe.outputItemType = output;
            recipe.outputQuantity = outputQuantity;
            recipe.durationTicks = durationTicks;
            _assets.Add(recipe);
            return recipe;
        }

        private (HandCrankBench bench, StorageBuffer stock) Build(params RecipeDefinition[] recipes)
        {
            var go = new GameObject("Bench");
            _spawned.Add(go);
            go.AddComponent<PlaceableBuilding>();

            var holderGo = new GameObject("Buffers");
            _spawned.Add(holderGo);
            var holder = holderGo.AddComponent<StorageBufferRegistryHolder>();

            HandCrankBench bench = go.AddComponent<HandCrankBench>();
            bench.Configure(holder, "FactoryStockpile", recipes);
            return (bench, holder.Registry.GetOrCreate("FactoryStockpile"));
        }

        // --- The admission rule ----------------------------------------------------------

        [Test]
        public void OnlySingleInputRecipesAreCrankable()
        {
            RecipeDefinition oneInput = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            RecipeDefinition twoInput = MakeRecipe("IronPlate", 2, 24, ("Scrap", 2), ("Coke", 1));

            Assert.IsTrue(HandCrankRules.IsCrankable(oneInput));
            Assert.IsFalse(HandCrankRules.IsCrankable(twoInput),
                "two distinct inputs is more than one person can feed by hand");
            Assert.IsFalse(HandCrankRules.IsCrankable(null));
        }

        [Test]
        public void AQuantityGreaterThanOneIsStillOneInput()
        {
            // R8 Gear Cutting is 2 Iron Plate -> 1 Gear. It is ONE thing to feed in, and §6
            // requires the Presser's ten Gears to be hand-made, so this must be crankable.
            RecipeDefinition gear = MakeRecipe("Gear", 1, 16, ("IronPlate", 2));
            Assert.IsTrue(HandCrankRules.IsCrankable(gear));
        }

        [Test]
        public void CrankTicks_IsExactlyFourTimesTheMachineDuration()
        {
            // 25 % speed. Exact in integers, so two players cranking the same recipe finish on
            // the same tick -- the discipline §1.4's burn accumulator established.
            Assert.AreEqual(96, HandCrankRules.CrankTicks(24));
            Assert.AreEqual(48, HandCrankRules.CrankTicks(12));
            Assert.AreEqual(64, HandCrankRules.CrankTicks(16));
            Assert.AreEqual(4, HandCrankRules.CrankTicks(0), "a zero duration must not mean a free craft");
        }

        // --- Cranking --------------------------------------------------------------------

        [Test]
        public void HoldingTheCrankForTheFullDurationProducesExactlyOneCraft()
        {
            RecipeDefinition r2 = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            var (bench, stock) = Build(r2);
            stock.Deposit("Scrap", 10);

            bench.IsCranking = true;
            for (int tick = 0; tick < HandCrankRules.CrankTicks(24); tick++)
            {
                bench.Tick(tick);
            }

            Assert.AreEqual(1, bench.CompletedCrafts);
            Assert.AreEqual(1, stock.GetQuantity("IronPlate"));
            Assert.AreEqual(9, stock.GetQuantity("Scrap"));
        }

        [Test]
        public void NotHoldingTheCrankDoesNothingAtAll()
        {
            RecipeDefinition r2 = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            var (bench, stock) = Build(r2);
            stock.Deposit("Scrap", 10);

            for (int tick = 0; tick < 500; tick++)
            {
                bench.Tick(tick);
            }

            // Held-Interact, not a machine: the manual era has to cost attention or it is just a
            // slower golem that needs no supervision.
            Assert.AreEqual(0, bench.CompletedCrafts);
            Assert.AreEqual(10, stock.GetQuantity("Scrap"));
        }

        [Test]
        public void ReleasingTheCrankPartWayCostsTimeButNeverGoods()
        {
            RecipeDefinition r2 = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            var (bench, stock) = Build(r2);
            stock.Deposit("Scrap", 5);

            bench.IsCranking = true;
            for (int tick = 0; tick < 50; tick++)
            {
                bench.Tick(tick);
            }

            bench.IsCranking = false;

            // Inputs are taken at completion, never up front -- a rigid game has no step that
            // could hand back goods consumed by a craft the player abandoned.
            Assert.AreEqual(0, bench.CompletedCrafts);
            Assert.AreEqual(5, stock.GetQuantity("Scrap"));
            Assert.AreEqual(0, stock.GetQuantity("IronPlate"));
        }

        [Test]
        public void CraftingWithoutTheInputsBanksNothingAndTakesNothing()
        {
            RecipeDefinition r2 = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            var (bench, stock) = Build(r2);

            Assert.IsFalse(bench.CanAffordSelected());

            bench.IsCranking = true;
            for (int tick = 0; tick < HandCrankRules.CrankTicks(24); tick++)
            {
                bench.Tick(tick);
            }

            Assert.AreEqual(0, bench.CompletedCrafts);
            Assert.AreEqual(0, stock.GetQuantity("IronPlate"));
        }

        [Test]
        public void ByproductsAreBankedToo()
        {
            RecipeDefinition withByproduct = MakeRecipe("Glass", 1, 20, ("Slag", 1));
            withByproduct.byproductItemType = "Coke";
            withByproduct.byproductQuantity = 2;
            var (bench, stock) = Build(withByproduct);
            stock.Deposit("Slag", 3);

            bench.IsCranking = true;
            for (int tick = 0; tick < HandCrankRules.CrankTicks(20); tick++)
            {
                bench.Tick(tick);
            }

            Assert.AreEqual(1, stock.GetQuantity("Glass"));
            Assert.AreEqual(2, stock.GetQuantity("Coke"));
        }

        [Test]
        public void AFullStockpileSlotLeavesTheInputsUntouched()
        {
            RecipeDefinition r2 = MakeRecipe("IronPlate", 1, 24, ("Scrap", 1));
            var (bench, stock) = Build(r2);
            stock.SetCapacityPerType(4);
            stock.Deposit("Scrap", 4);
            stock.Deposit("IronPlate", 4); // the output slot is now full

            bench.IsCranking = true;
            for (int tick = 0; tick < HandCrankRules.CrankTicks(24); tick++)
            {
                bench.Tick(tick);
            }

            // Room is confirmed before anything is withdrawn -- §1.3's atomicity, so a full slot
            // stalls rather than swallowing the Scrap.
            Assert.AreEqual(0, bench.CompletedCrafts);
            Assert.AreEqual(4, stock.GetQuantity("Scrap"));
            Assert.AreEqual(4, stock.GetQuantity("IronPlate"));
        }

        // --- Selection -------------------------------------------------------------------

        [Test]
        public void OnlyCrankableRecipesAppearAndCyclingWraps()
        {
            RecipeDefinition coke = MakeRecipe("Coke", 1, 12, ("Coal", 1));
            RecipeDefinition smelt = MakeRecipe("IronPlate", 2, 24, ("Scrap", 2), ("Coke", 1));
            RecipeDefinition gear = MakeRecipe("Gear", 1, 16, ("IronPlate", 2));
            var (bench, _) = Build(coke, smelt, gear);

            Assert.AreEqual(2, bench.CrankableRecipes.Count, "the 2-input smelt must not be offered");
            Assert.AreEqual(coke, bench.SelectedRecipe);

            bench.CycleRecipe();
            Assert.AreEqual(gear, bench.SelectedRecipe);

            bench.CycleRecipe();
            Assert.AreEqual(coke, bench.SelectedRecipe, "cycling wraps");
        }

        [Test]
        public void CyclingMidCraftAbandonsProgressWithoutCostingGoods()
        {
            RecipeDefinition coke = MakeRecipe("Coke", 1, 12, ("Coal", 1));
            RecipeDefinition gear = MakeRecipe("Gear", 1, 16, ("IronPlate", 2));
            var (bench, stock) = Build(coke, gear);
            stock.Deposit("Coal", 5);

            bench.IsCranking = true;
            for (int tick = 0; tick < 30; tick++)
            {
                bench.Tick(tick);
            }

            Assert.Greater(bench.ProgressTicks, 0);
            bench.CycleRecipe();

            Assert.AreEqual(0, bench.ProgressTicks);
            Assert.AreEqual(5, stock.GetQuantity("Coal"));
        }

        [Test]
        public void ABenchWithNothingCrankableIsInertRatherThanBroken()
        {
            RecipeDefinition smelt = MakeRecipe("IronPlate", 2, 24, ("Scrap", 2), ("Coke", 1));
            var (bench, _) = Build(smelt);

            Assert.IsNull(bench.SelectedRecipe);
            Assert.AreEqual(0, bench.RequiredTicks);
            Assert.IsFalse(bench.CanAffordSelected());

            bench.IsCranking = true;
            bench.Tick(0);
            bench.CycleRecipe();

            Assert.AreEqual(0, bench.CompletedCrafts);
        }

        // --- §10's blackout backstop -----------------------------------------------------

        [Test]
        public void TheBenchIsUnpoweredByDesignAndCanAlwaysRestartADeadFactory()
        {
            // §10 clears "total blackout with no golems to recover" ONLY because the bench runs
            // R1 without steam. There is deliberately no steam network anywhere in this test --
            // if the bench ever grows a power precondition, this fails and the audit row with it.
            RecipeDefinition r1 = MakeRecipe("Coke", 1, 12, ("Coal", 1));
            var (bench, stock) = Build(r1);
            stock.Deposit("Coal", 3);

            bench.IsCranking = true;
            for (int tick = 0; tick < HandCrankRules.CrankTicks(12) * 3; tick++)
            {
                bench.Tick(tick);
            }

            Assert.AreEqual(3, bench.CompletedCrafts);
            Assert.AreEqual(3, stock.GetQuantity("Coke"),
                "with no steam anywhere, the player must still be able to hand-crank coke");
        }

        [Test]
        public void TheBenchHoldsNoReferenceToSteamAtAll()
        {
            // Structural, not behavioural: the guarantee above is only durable if the type
            // genuinely cannot consult power. Cheap to assert and impossible to forget.
            foreach (var field in typeof(HandCrankBench).GetFields(
                         System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Public))
            {
                StringAssert.DoesNotContain("Steam", field.FieldType.Name,
                    "the Hand-Crank Bench must be explicitly unpowered (§10)");
            }
        }
    }
}
