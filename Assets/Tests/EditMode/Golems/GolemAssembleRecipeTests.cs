using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Multi-input Assemble: docs/progression-design.md §5.2 ("What multi-input assembly must
    // support") and §11 item 2. A RecipeDefinition carries 1-4 typed inputs with quantities, an
    // output whose quantity may exceed 1, and exactly one optional byproduct. Assemble checks
    // every input against the golem's own input stock ATOMICALLY, checks room for everything it
    // will produce BEFORE consuming anything, and never touches a tile.
    //
    // GolemMachineModelTests pins the single-input behaviours that survived from §1.1; this file
    // is about what having several inputs adds -- atomicity, arity, output quantity, byproducts
    // and the shortfall stall §8 asks for by name.
    public class GolemAssembleRecipeTests
    {
        // Stand-ins for the real goods until §1.5 authors the item list. Bare strings, matching
        // the project's id convention; ItemType only holds the three that already exist.
        private const string Coke = "Coke";
        private const string IronPlate = "IronPlate";
        private const string Casing = "Casing";
        private const string Slag = "Slag";
        private const string Mechanism = "Mechanism";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private static RecipeDefinition Recipe(
            string outputItemType, int outputQuantity, int durationTicks,
            params RecipeIngredient[] inputs)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.outputItemType = outputItemType;
            recipe.outputQuantity = outputQuantity;
            recipe.durationTicks = durationTicks;
            foreach (RecipeIngredient ingredient in inputs)
            {
                recipe.inputs.Add(ingredient);
            }
            return recipe;
        }

        private static RecipeIngredient In(string itemType, int quantity) =>
            new RecipeIngredient(itemType, quantity);

        private static AppendageActionDefinition AssembleCard(RecipeDefinition recipe)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.Assemble;
            // Left deliberately populated and deliberately wrong. §1.1 borrowed these two fields
            // as an explicit placeholder for a single-input Assemble; §1.3 must stop consulting
            // them entirely, and the only way to prove that is to have them disagree.
            step.inputItemType = "NotARealInput";
            step.outputItemType = "NotARealOutput";
            step.durationTicks = 999;
            step.recipe = recipe;
            return step;
        }

        private GolemEntity CreateGolem(params AppendageActionDefinition[] steps)
        {
            var go = new GameObject("AssemblerGolem");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure("Assembler", null);

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            foreach (AppendageActionDefinition step in steps)
            {
                entity.Program.appendages.Add(step);
            }
            return entity;
        }

        private static void Run(GolemEntity golem, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                golem.Tick(tick);
            }
        }

        // --- Atomicity: the core claim of the whole item ---------------------------------------

        [Test]
        public void Assemble_ShortOnItsSecondInput_WithdrawsNothingAtAll()
        {
            // THE test for this item. A partial withdrawal on a recipe that then stalls strands
            // goods inside the golem FOREVER: a rigid program has no step that could put them
            // back, and nothing outside the golem can reach its input stock. The first input
            // must still be sitting there untouched.
            RecipeDefinition recipe = Recipe(IronPlate, 2, 24,
                In(ItemType.Scrap, 2), In(Coke, 1));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 5);
            // No Coke at all.

            Run(golem, 5);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(5, golem.Inventory.GetInput(ItemType.Scrap),
                "the first input was consumed before the recipe discovered the second was short");
            Assert.AreEqual(0, golem.Inventory.GetOutput(IronPlate));
        }

        [Test]
        public void Assemble_ShortOnItsThirdInput_LeavesTheFirstTwoUntouched()
        {
            RecipeDefinition recipe = Recipe(Mechanism, 1, 60,
                In(ItemType.Brass, 3), In(IronPlate, 2), In(Casing, 1));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Brass, 3);
            golem.Inventory.AddInput(IronPlate, 2);

            Run(golem, 5);

            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Brass));
            Assert.AreEqual(2, golem.Inventory.GetInput(IronPlate));
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(Casing, golem.StallResourceId);
        }

        // --- Arity: 1, 2, 3 and 4 inputs all run ------------------------------------------------

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Assemble_RunsRecipesOfEveryAuthorableArity(int inputCount)
        {
            // §5.2 caps inputs at 4 because §2's Consequence 1 makes slots = inputs + 2 and the
            // Zeppelin, the largest chassis, has 6.
            string[] types = { ItemType.Scrap, Coke, ItemType.Brass, Casing };

            RecipeDefinition recipe = Recipe(Mechanism, 1, 2);
            for (int i = 0; i < inputCount; i++)
            {
                recipe.inputs.Add(In(types[i], i + 1));
            }

            Assert.IsTrue(recipe.IsWellFormed(), inputCount + " inputs was rejected as malformed");

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            for (int i = 0; i < inputCount; i++)
            {
                golem.Inventory.AddInput(types[i], i + 1);
            }

            Run(golem, 2);

            Assert.AreEqual(1, golem.Inventory.GetOutput(Mechanism));
            for (int i = 0; i < inputCount; i++)
            {
                Assert.AreEqual(0, golem.Inventory.GetInput(types[i]),
                    "input " + types[i] + " was not fully consumed");
            }
        }

        [Test]
        public void Assemble_ConsumesEveryInputAtItsOwnAuthoredQuantity()
        {
            // R9's shape: 4 Iron Plate + 1 Brass -> 1 Casing. Per-input quantities, not a
            // shared one.
            RecipeDefinition recipe = Recipe(Casing, 1, 28,
                In(IronPlate, 4), In(ItemType.Brass, 1));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(IronPlate, 10);
            golem.Inventory.AddInput(ItemType.Brass, 10);

            golem.Tick(0);

            Assert.AreEqual(6, golem.Inventory.GetInput(IronPlate));
            Assert.AreEqual(9, golem.Inventory.GetInput(ItemType.Brass));
        }

        // --- Output quantity and byproducts -----------------------------------------------------

        [Test]
        public void Assemble_WithAnOutputQuantityAboveOne_DepositsTheFullAmount()
        {
            // R7 makes 3 Brass from 2 Copper Ingot + 1 Zinc Ingot; R4 makes 2 Iron Plate; R19
            // makes 3 Copper Wire. An output quantity of 1 is the exception, not the rule.
            RecipeDefinition recipe = Recipe(ItemType.Brass, 3, 1, In(ItemType.Scrap, 2));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 2);

            golem.Tick(0);

            Assert.AreEqual(3, golem.Inventory.GetOutput(ItemType.Brass));
        }

        [Test]
        public void Assemble_DepositsTheByproductAlongsideTheOutput_AndOnlyAtCompletion()
        {
            // R4 exactly: 2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag.
            RecipeDefinition recipe = Recipe(IronPlate, 2, 3, In(ItemType.Scrap, 2), In(Coke, 1));
            recipe.byproductItemType = Slag;
            recipe.byproductQuantity = 1;

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 2);
            golem.Inventory.AddInput(Coke, 1);

            golem.Tick(0);
            Assert.AreEqual(0, golem.Inventory.GetOutput(IronPlate), "the product appeared at begin");
            Assert.AreEqual(0, golem.Inventory.GetOutput(Slag), "the byproduct appeared at begin");

            golem.Tick(1);
            golem.Tick(2);

            Assert.AreEqual(2, golem.Inventory.GetOutput(IronPlate));
            Assert.AreEqual(1, golem.Inventory.GetOutput(Slag));
        }

        // --- Room for the products, checked before anything is consumed -------------------------

        [Test]
        public void Assemble_WithoutRoomForTheWholeOutput_StallsOutputFullWithInputsIntact()
        {
            // Partial room is not room: a recipe making 2 must not run into a stock with 1 slot
            // left and quietly lose the second unit to GolemInventory.Stock.Add's clamp.
            RecipeDefinition recipe = Recipe(IronPlate, 2, 1, In(ItemType.Scrap, 2));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 4);
            golem.Inventory.AddOutput(IronPlate, GolemInventory.CapacityPerType - 1);

            Run(golem, 5);

            Assert.AreEqual(StallReason.OutputFull, golem.StallReason);
            Assert.AreEqual(IronPlate, golem.StallResourceId);
            Assert.AreEqual(4, golem.Inventory.GetInput(ItemType.Scrap), "the inputs were eaten anyway");
            Assert.AreEqual(GolemInventory.CapacityPerType - 1, golem.Inventory.GetOutput(IronPlate));
        }

        [Test]
        public void Assemble_WithoutRoomForTheByproduct_StallsNamingTheByproduct_WithInputsIntact()
        {
            // progression-design §5.3(c): a backed-up Slag slot stalls the smelter. The product
            // has plenty of room -- it is the byproduct that has none, and the stall has to say
            // so or the player has no clue a disposal route is what the line is waiting on.
            RecipeDefinition recipe = Recipe(IronPlate, 2, 1, In(ItemType.Scrap, 2), In(Coke, 1));
            recipe.byproductItemType = Slag;
            recipe.byproductQuantity = 1;

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 4);
            golem.Inventory.AddInput(Coke, 2);
            golem.Inventory.AddOutput(Slag, GolemInventory.CapacityPerType);

            Run(golem, 5);

            Assert.AreEqual(StallReason.OutputFull, golem.StallReason);
            Assert.AreEqual(Slag, golem.StallResourceId,
                "it named the product, which is not what is blocked");
            Assert.AreEqual(4, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(2, golem.Inventory.GetInput(Coke));
            Assert.AreEqual(0, golem.Inventory.GetOutput(IronPlate),
                "it produced the output despite having nowhere to put the byproduct");
        }

        // --- The shortfall stall (progression-design §8) -----------------------------------------

        [Test]
        public void Assemble_ShortfallStall_NamesTheTypeAndHowManyMoreAreNeeded()
        {
            // R15's shape: on 10 Casing, "no Casing available" cannot tell the player whether
            // they are one short or nine, and those are completely different problems.
            RecipeDefinition recipe = Recipe("FrameSection", 1, 90,
                In(Casing, 10), In(IronPlate, 6), In(ItemType.Brass, 4));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(Casing, 1);

            golem.Tick(0);

            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(Casing, golem.StallResourceId,
                "the resource id must stay the BARE item type -- InputFull/OutputFull set that " +
                "convention and the badge, the strip and the tests all read it that way");
            Assert.AreEqual(9, golem.StallShortfall, "it reported the recipe's quantity, not the gap");
        }

        [Test]
        public void Assemble_WithTwoShortInputs_NamesTheFirstInTheRecipesAuthoredOrder()
        {
            // Deterministic by construction: two identically-programmed golems must produce the
            // same diagnosis, so the choice cannot depend on which shortfall is larger or on
            // dictionary ordering. Authored order is the only ordering both provably share.
            RecipeDefinition recipe = Recipe(Mechanism, 1, 60,
                In(Casing, 2), In(IronPlate, 8));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            // IronPlate is short by far more (8), but Casing is authored first.
            golem.Inventory.AddInput(Casing, 1);

            golem.Tick(0);

            Assert.AreEqual(Casing, golem.StallResourceId);
            Assert.AreEqual(1, golem.StallShortfall);
        }

        [Test]
        public void Assemble_ShortfallStall_PublishesTheAmountOnTheEvent()
        {
            // The badge and the alerts strip read the event, not the entity, in the common case.
            RecipeDefinition recipe = Recipe(Casing, 1, 28, In(IronPlate, 4));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(IronPlate, 1);

            var published = new List<GolemStalledEvent>();
            System.Action<GolemStalledEvent> handler = e => published.Add(e);
            EventBus.GolemStalled += handler;
            try
            {
                golem.Tick(0);
            }
            finally
            {
                EventBus.GolemStalled -= handler;
            }

            Assert.AreEqual(1, published.Count);
            Assert.AreEqual(StallReason.MissingItem, published[0].Reason);
            Assert.AreEqual(IronPlate, published[0].ResourceId);
            Assert.AreEqual(3, published[0].Shortfall);
        }

        [Test]
        public void StallShortfall_IsZeroForStallsThatCarryNoAmount()
        {
            // A Haul against a tile holding the wrong good is also MissingItem, but there is no
            // "how many more" to report -- the type simply is not there. The text has to fall
            // back to what it always said, so the amount must not leak in from anywhere.
            var haul = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            haul.actionType = AppendageActionType.Haul;
            haul.inputItemType = ItemType.Aether;

            var endpointsGO = new GameObject("SpatialEndpointRegistryHolder");
            _spawned.Add(endpointsGO);
            var endpoints = endpointsGO.AddComponent<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 10);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(haul);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(0, golem.StallShortfall);
        }

        [Test]
        public void StallShortfall_DoesNotSurviveIntoALaterStepsStall()
        {
            // Step 0 stalls with an amount; once it clears, step 1 must not inherit it.
            RecipeDefinition first = Recipe(Casing, 1, 1, In(IronPlate, 2));
            RecipeDefinition second = Recipe(Mechanism, 1, 1, In(ItemType.Brass, 1));

            GolemEntity golem = CreateGolem(AssembleCard(first), AssembleCard(second));
            golem.Inventory.AddInput(ItemType.Brass, 5);
            golem.Inventory.AddOutput(Mechanism, GolemInventory.CapacityPerType);

            golem.Tick(0);
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(2, golem.StallShortfall);

            // Supply what step 0 was waiting on: it now runs and hands over to step 1, which is
            // blocked on output room -- a stall with no amount to report.
            golem.Inventory.AddInput(IronPlate, 2);
            golem.Tick(1);
            golem.Tick(2);

            Assert.AreEqual(StallReason.OutputFull, golem.StallReason);
            Assert.AreEqual(0, golem.StallShortfall, "the previous step's shortfall leaked forward");
        }

        // --- Malformed and missing recipes ------------------------------------------------------
        // All of these must be an honest stall. BeginStep runs inside Tick, where a throw takes
        // the simulation clock down with it.

        [Test]
        public void Assemble_WithANullRecipe_StallsUnconfigured()
        {
            GolemEntity golem = CreateGolem(AssembleCard(null));

            golem.Tick(0);

            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
        }

        [Test]
        public void Assemble_WithARecipeThatHasNoInputs_StallsUnconfigured()
        {
            RecipeDefinition recipe = Recipe(IronPlate, 1, 4);

            GolemEntity golem = CreateGolem(AssembleCard(recipe));

            Assert.IsFalse(recipe.IsWellFormed());
            Run(golem, 3);

            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
            Assert.AreEqual(0, golem.Inventory.GetOutput(IronPlate),
                "a recipe with no inputs produced something from nothing");
        }

        [Test]
        public void Assemble_WithADuplicateInputType_StallsUnconfigured()
        {
            // Silently summing the two would make the authored recipe and the executed one
            // different things, which is the sort of divergence that only shows up as a ratio
            // that never balances.
            RecipeDefinition recipe = Recipe(IronPlate, 1, 4,
                In(ItemType.Scrap, 2), In(ItemType.Scrap, 3));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 10);

            Run(golem, 3);

            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
            Assert.AreEqual(10, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void Assemble_WithABlankOutput_StallsUnconfigured()
        {
            RecipeDefinition recipe = Recipe(null, 1, 4, In(ItemType.Scrap, 1));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 5);

            Run(golem, 3);

            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
            Assert.AreEqual(5, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void Assemble_IgnoresTheCardsOwnInputAndOutputItemType()
        {
            // AssembleCard fills both with deliberate nonsense. If Assemble still read either
            // one, this recipe could not run.
            RecipeDefinition recipe = Recipe(IronPlate, 1, 1, In(ItemType.Scrap, 1));

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(ItemType.Scrap, 1);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(1, golem.Inventory.GetOutput(IronPlate));
            Assert.AreEqual(0, golem.Inventory.GetOutput("NotARealOutput"));
        }

        // --- The per-type input cap still binds ---------------------------------------------------

        [Test]
        public void Assemble_NeedingMoreOfOneTypeThanTheGolemCanHold_StallsHonestlyForever()
        {
            // §6: GolemInventory.CapacityPerType (12) is a hard ceiling on any single input, and
            // that is deliberate -- it is what will make Repeat(n) on R15 (10 Casing) impossible.
            // A recipe past the ceiling must not crash, must not be silently rewritten, and must
            // not report "not wired up" either: the honest answer is the shortfall it can never
            // close, which is the one thing that tells the player what happened.
            const int impossible = GolemInventory.CapacityPerType + 1;
            RecipeDefinition recipe = Recipe(Casing, 1, 4, In(IronPlate, impossible));

            Assert.IsTrue(recipe.IsWellFormed(),
                "an over-capacity quantity is unsatisfiable, not malformed -- turning it into " +
                "Unconfigured would hide the number the player needs");

            GolemEntity golem = CreateGolem(AssembleCard(recipe));
            golem.Inventory.AddInput(IronPlate, GolemInventory.CapacityPerType);

            Run(golem, 20);

            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(IronPlate, golem.StallResourceId);
            Assert.AreEqual(1, golem.StallShortfall);
            Assert.AreEqual(GolemInventory.CapacityPerType, golem.Inventory.GetInput(IronPlate),
                "it kept nibbling at a stock it can never top up");
        }

        // --- RecipeDefinition validation, directly ------------------------------------------------
        // Validation belongs at the authoring edge; these pin what counts as an authoring error
        // and that the reason is reported rather than thrown.

        [Test]
        public void IsWellFormed_AcceptsAFourInputRecipeWithAByproduct()
        {
            RecipeDefinition recipe = Recipe("Regulator", 1, 56,
                In("AetherCell", 1), In(ItemType.Brass, 2), In("Gear", 1), In("CopperWire", 4));
            recipe.byproductItemType = Slag;

            string problem;
            Assert.IsTrue(recipe.IsWellFormed(out problem), problem);
            Assert.IsNull(problem);
            Assert.IsTrue(recipe.HasByproduct);
        }

        [Test]
        public void IsWellFormed_RejectsAFifthInputType()
        {
            // slots = inputs + 2, and the Zeppelin -- the largest chassis -- has 6.
            RecipeDefinition recipe = Recipe(Mechanism, 1, 10,
                In("A", 1), In("B", 1), In("C", 1), In("D", 1), In("E", 1));

            string problem;
            Assert.IsFalse(recipe.IsWellFormed(out problem));
            Assert.IsNotNull(problem);
        }

        [Test]
        public void IsWellFormed_RejectsANonPositiveInputQuantity()
        {
            // Consuming nothing would stall forever with no shortfall to name, which reads to
            // the player as a bug rather than as a recipe.
            RecipeDefinition recipe = Recipe(IronPlate, 1, 4, In(ItemType.Scrap, 0));

            Assert.IsFalse(recipe.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_RejectsAByproductThatRepeatsTheOutputType()
        {
            // They would share one per-type slot in output stock, so the two independent room
            // checks in BeginAssemble would both pass with room for only one of them.
            RecipeDefinition recipe = Recipe(IronPlate, 1, 4, In(ItemType.Scrap, 1));
            recipe.byproductItemType = IronPlate;

            Assert.IsFalse(recipe.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_RejectsANonPositiveOutputQuantity()
        {
            RecipeDefinition recipe = Recipe(IronPlate, 0, 4, In(ItemType.Scrap, 1));

            Assert.IsFalse(recipe.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_TreatsABlankByproductAsNoByproduct()
        {
            RecipeDefinition recipe = Recipe(IronPlate, 1, 4, In(ItemType.Scrap, 1));

            Assert.IsTrue(recipe.IsWellFormed());
            Assert.IsFalse(recipe.HasByproduct);
        }
    }
}
