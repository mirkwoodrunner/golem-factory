using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The machine model: docs/progression-design.md section 2. A spatially placed golem is a
    // machine with an internal typed inventory -- Haul/ExtractFromNode fill its input stock from
    // the tile behind, Assemble converts input to output without touching a tile at all, and
    // Push empties stock onto the tile in front.
    //
    // The model rides on the EXISTING IsSpatiallyPlaced fork. An id-routed golem (Main.unity's
    // seven hand-wired demos, and every pre-existing test) keeps the old semantics byte for
    // byte, including step.durationTicks as its duration -- pinned at the bottom of this file.
    public class GolemMachineModelTests
    {
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

        private T AddHolder<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private static AppendageActionDefinition Step(
            AppendageActionType type, string inputItemType = null, string outputItemType = null,
            int durationTicks = 1)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = type;
            step.inputItemType = inputItemType;
            step.outputItemType = outputItemType;
            step.durationTicks = durationTicks;
            return step;
        }

        // §1.3: Assemble reads a RecipeDefinition and nothing else, so the Assemble steps below
        // are built from one -- the inputItemType/outputItemType pair they used to borrow was an
        // explicit §1.1 placeholder. Built with CreateInstance the same way Step builds an
        // appendage; no .asset is authored for a test.
        private static AppendageActionDefinition AssembleStep(
            string inputItemType, string outputItemType, int durationTicks = 1,
            int inputQuantity = 1, int outputQuantity = 1,
            string byproductItemType = null, int byproductQuantity = 1)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.Assemble;
            // Deliberately absurd, and deliberately present: nothing about an Assemble may be
            // read off the card any more.
            step.durationTicks = 999;

            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.inputs.Add(new RecipeIngredient(inputItemType, inputQuantity));
            recipe.outputItemType = outputItemType;
            recipe.outputQuantity = outputQuantity;
            recipe.byproductItemType = byproductItemType;
            recipe.byproductQuantity = byproductQuantity;
            recipe.durationTicks = durationTicks;
            step.recipe = recipe;
            return step;
        }

        private GolemEntity CreateGolem(params AppendageActionDefinition[] steps)
        {
            var go = new GameObject("MachineGolem");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure("Machine", null);

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

        // Ticks until the program wraps back to its first step, which is what the derived
        // durations are actually observable as.
        private static int TicksToFirstCompletion(GolemEntity golem, int maxTicks = 200)
        {
            int completions = 0;
            Action<GolemCompletedEvent> handler = delegate { completions++; };
            EventBus.GolemCompleted += handler;
            try
            {
                for (int tick = 0; tick < maxTicks; tick++)
                {
                    golem.Tick(tick);
                    if (completions > 0)
                    {
                        return tick + 1;
                    }
                }
            }
            finally
            {
                EventBus.GolemCompleted -= handler;
            }

            return -1;
        }

        // --- The pure-logistics rule ---------------------------------------------------------
        // "A program containing no Assemble step treats its input stock as its output stock."
        // Without it, every logistics golem in the game -- the Scavenger, every node extractor,
        // every belt-to-buffer golem, roughly a quarter of the endgame factory -- hauls into
        // input, pushes an empty output, fills to the per-type cap and stalls forever.

        [Test]
        public void PureLogistics_HaulThenPush_CyclesIndefinitelyWithoutStalling()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 500);
            var destination = new StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(
                Step(AppendageActionType.Haul, ItemType.Scrap), Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 4);

            // Far more than the 12-per-type cap: a golem hauling into a stock nothing drains
            // would jam within four cycles.
            for (int tick = 0; tick < 400; tick++)
            {
                golem.Tick(tick);
                Assert.AreNotEqual(GolemState.Stalled, golem.Program.State,
                    "the pure-logistics rule is missing -- it jammed at tick " + tick +
                    " with " + golem.StallReason);
            }

            Assert.Greater(destination.GetQuantity(ItemType.Scrap), GolemInventory.CapacityPerType,
                "it never actually delivered past one hold's worth");
        }

        [Test]
        public void PureLogistics_PushDrainsTheInputStock()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            golem.Tick(0);

            Assert.AreEqual(3, destination.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void WithAnAssembleStep_PushDrainsOutputAndLeavesInputAlone()
        {
            // The other half of the rule: as soon as a program has an Assemble, input stock is
            // working material and only the product may leave.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 1),
                Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 4);

            Run(golem, 2);

            Assert.AreEqual(1, destination.GetQuantity(ItemType.Brass));
            Assert.AreEqual(0, destination.GetQuantity(ItemType.Scrap),
                "Push emptied the input stock on a program that has an Assemble");
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap));
        }

        // --- Haul ------------------------------------------------------------------------------

        [Test]
        public void Haul_TakesTheAuthoredTypeIntoInputStock()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Mixed");
            source.Deposit(ItemType.Scrap, 10);
            source.Deposit(ItemType.Brass, 10);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Brass));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 3);

            golem.Tick(0);

            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Brass));
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Scrap), "it grabbed the wrong good");
            Assert.AreEqual(10, source.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Haul_TakesWhatIsThere_WhenTheTileHoldsLessThanTheBatch()
        {
            // Partial takes are deliberate: stalling a golem that can still make progress would
            // deadlock every under-supplied line in the factory.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 2);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 8);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(2, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void Haul_OfATypeTheTileDoesNotHold_StallsMissingItemNamingTheType()
        {
            // The tile is not empty -- it is full of the wrong good. Reporting BufferEmpty here
            // would point the player at a buffer they can see is stocked; the actionable fact
            // is which type is absent, so they look upstream of it instead.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 10);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Aether));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State,
                "a typed Haul silently took the wrong good");
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(ItemType.Aether, golem.StallResourceId,
                "the stall named the endpoint rather than the type that is missing");
            Assert.AreEqual(10, source.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Scrap));
        }

        // --- The other side of that split, pinned so nobody collapses it back ----------------
        // A genuinely empty source is a different problem with a different fix ("wait, or
        // rotate me"), so it must keep naming the ENDPOINT, not an item type.

        [Test]
        public void Haul_FromAGenuinelyEmptyBuffer_StillStallsBufferEmptyNamingTheEndpoint()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 0),
                new StorageBufferEndpoint(new StorageBuffer("EmptyChest")));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.BufferEmpty, golem.StallReason);
            Assert.AreEqual("EmptyChest", golem.StallResourceId);
        }

        [Test]
        public void Haul_FromABeltWhoseItemHasNotArrivedYet_StallsBeltEmptyNamingTheEndpoint()
        {
            // An in-transit item is "not there yet", not "the wrong type" -- TryPeekHead
            // correctly refuses it, so PeekAvailableType is null and the endpoint gets named.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("TileBelt", 3);
            belt.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            endpoints.Registry.Register(new Vector2Int(0, 0), new BeltSegmentEndpoint(belt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.BeltEmpty, golem.StallReason);
            Assert.AreEqual("TileBelt", golem.StallResourceId);
        }

        [Test]
        public void Haul_FromADepletedNode_StillStallsNodeEmptyNamingTheEndpoint()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 0),
                new ResourceNodeEndpoint(new ResourceNode("SpentNode", ItemType.Scrap, 0)));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            Assert.AreEqual("SpentNode", golem.StallResourceId);
        }

        [Test]
        public void Haul_ClampsTheTakeToTheRoomLeftInStock_RatherThanDroppingTheOverflow()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 100);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, GolemInventory.CapacityPerType);
            golem.Inventory.AddInput(ItemType.Scrap, GolemInventory.CapacityPerType - 2);

            golem.Tick(0);

            Assert.AreEqual(GolemInventory.CapacityPerType, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(98, source.GetQuantity(ItemType.Scrap),
                "it pulled more out of the tile than it could hold, and dropped the difference");
        }

        [Test]
        public void Haul_WithAFullInputStock_StallsInputFullNamingTheItemType()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 100);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, GolemInventory.CapacityPerType);

            golem.Tick(0);

            Assert.AreEqual(StallReason.InputFull, golem.StallReason);
            Assert.AreEqual(ItemType.Scrap, golem.StallResourceId,
                "the stall must name the blocked good, not the tile behind");
            Assert.AreEqual(100, source.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Haul_WithNoAuthoredType_TakesWhateverTheTileOffers()
        {
            // HaulScrap.asset and every ExtractFromNode card predate typed Haul and carry no
            // inputItemType. Blank has to keep meaning "whatever is behind me".
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Aether, 5);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(1, golem.Inventory.GetInput(ItemType.Aether));
        }

        // --- Derived durations (progression-design section 2's cycle-time table) ---------------

        [Test]
        public void Haul_TakesMaxOfTwoAndTheBatchSize()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 500);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));

            // durationTicks is deliberately absurd -- the authored value must be ignored here.
            GolemEntity small = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap, durationTicks: 40));
            small.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            small.Program.SetQuantityAt(0, 1);
            Assert.AreEqual(2, TicksToFirstCompletion(small), "max(2, 1) should floor at 2");

            GolemEntity large = CreateGolem(Step(AppendageActionType.Haul, ItemType.Scrap, durationTicks: 40));
            large.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            large.Program.SetQuantityAt(0, 5);
            Assert.AreEqual(5, TicksToFirstCompletion(large), "max(2, 5) should be 5");
        }

        [Test]
        public void ExtractFromNode_TakesSixPlusTheBatchSize()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 0),
                new ResourceNodeEndpoint(new ResourceNode("N", ItemType.Scrap, ResourceNode.Infinite)));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, durationTicks: 40));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 4);

            Assert.AreEqual(10, TicksToFirstCompletion(golem));
            Assert.AreEqual(4, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void Push_TakesTwoPlusTheUnitsItActuallyMoved()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(new StorageBuffer("Dest")));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push, durationTicks: 40));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 2);
            golem.Inventory.AddInput(ItemType.Brass, 1);

            Assert.AreEqual(5, TicksToFirstCompletion(golem), "2 + 3 units");
        }

        [Test]
        public void Push_ChargesForWhatLeft_NotForWhatItHopedToSend()
        {
            // A belt that only had room for one must not also bill the golem for the two units
            // still sitting in its hold.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("TileBelt", 1);
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(belt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push, durationTicks: 40));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            Assert.AreEqual(3, TicksToFirstCompletion(golem), "2 + the 1 unit the belt took");
        }

        // --- Push ------------------------------------------------------------------------------

        [Test]
        public void Push_EmptiesEveryTypeInOneStep()
        {
            // Consequence 3: a smelter emitting Iron Plate *and* Slag costs one Push, not two,
            // which is what keeps its recipe inside the chassis' slot count.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 2);
            golem.Inventory.AddInput(ItemType.Brass, 3);
            golem.Inventory.AddInput(ItemType.Aether, 1);

            golem.Tick(0);

            Assert.AreEqual(2, destination.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(3, destination.GetQuantity(ItemType.Brass));
            Assert.AreEqual(1, destination.GetQuantity(ItemType.Aether));
            Assert.AreEqual(0, golem.Inventory.Input.TotalUnits);
        }

        [Test]
        public void Push_WhenTheTargetFillsPartway_KeepsTheRemainderInStock()
        {
            // The no-item-loss invariant, at the far end of the golem. A unit is only removed
            // from stock once the destination has accepted it, so there is no window in which
            // it exists in neither place.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("TileBelt", 1);
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(belt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            golem.Tick(0);

            Assert.AreEqual(1, belt.Items.Count);
            Assert.AreEqual(2, golem.Inventory.GetInput(ItemType.Scrap),
                "the units the belt refused were destroyed");
            Assert.AreEqual(StallReason.None, golem.StallReason,
                "a partial push is progress, not a stall");
        }

        [Test]
        public void Push_WithNowhereToPushTo_StallsWithoutLosingAnything()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            Run(golem, 5);

            Assert.AreEqual(StallReason.NoTargetAtTile, golem.StallReason);
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void Push_OnAGolemThatWasNeverPlaced_StallsUnconfigured()
        {
            // Push is a brand-new verb with no legacy call sites, so it gets an honest failure
            // rather than the no-op success Haul had to keep for compatibility.
            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap));
        }

        // --- Assemble ---------------------------------------------------------------------------

        [Test]
        public void Assemble_ConsumesInputAtBegin_AndDepositsOutputOnlyAtCompletion()
        {
            GolemEntity golem = CreateGolem(
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 3));
            golem.Inventory.AddInput(ItemType.Scrap, 2);

            golem.Tick(0);
            Assert.AreEqual(1, golem.Inventory.GetInput(ItemType.Scrap),
                "the precursor should be committed up front");
            Assert.AreEqual(0, golem.Inventory.GetOutput(ItemType.Brass),
                "the product appeared before the recipe had finished running");

            golem.Tick(1);
            Assert.AreEqual(0, golem.Inventory.GetOutput(ItemType.Brass));

            golem.Tick(2);
            Assert.AreEqual(1, golem.Inventory.GetOutput(ItemType.Brass));
        }

        [Test]
        public void Assemble_NeedsNoSpatialPlacementAndTouchesNoTile()
        {
            // It reads a typed dictionary the golem owns, which is the whole reason the machine
            // model was worth adopting -- no tile take can transmute the wrong good.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var behind = new StorageBuffer("Behind");
            behind.Deposit(ItemType.Aether, 5);
            var inFront = new StorageBuffer("InFront");
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(behind));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(inFront));

            GolemEntity golem = CreateGolem(
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 1));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 1);

            golem.Tick(0);

            Assert.AreEqual(1, golem.Inventory.GetOutput(ItemType.Brass));
            Assert.AreEqual(5, behind.GetQuantity(ItemType.Aether), "it took from the tile behind");
            Assert.AreEqual(0, inFront.GetQuantity(ItemType.Brass), "it wrote to the tile in front");
        }

        [Test]
        public void Assemble_WithoutThePrecursor_StallsMissingItemNamingTheShortfall()
        {
            // Three assemblers on one line waiting on three different precursors have to be
            // distinguishable; "GolemA stalled: GolemA has no input" is not.
            GolemEntity golem = CreateGolem(
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 2, inputQuantity: 2));

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(ItemType.Scrap, golem.StallResourceId);
            // §1.3 / progression-design §8: the amount rides alongside the type rather than
            // being encoded into the resource id, which stays the bare good.
            Assert.AreEqual(2, golem.StallShortfall,
                "the stall named the missing type but not how many more are needed");
            Assert.AreEqual(0, golem.Inventory.GetOutput(ItemType.Brass));
        }

        [Test]
        public void Push_WithAnEmptyHold_StallsWithoutNamingTheGolemOrAnAbsentSource()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(new StorageBuffer("Dest")));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.IsNull(golem.StallResourceId, "Push names no item type, so it must name none");
        }

        [Test]
        public void Assemble_WithAFullOutputStock_StallsOutputFullWithoutEatingThePrecursor()
        {
            // Ordering discipline: check there is room for the product BEFORE consuming the
            // ingredient, or a jammed output silently destroys material every tick.
            GolemEntity golem = CreateGolem(
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 1));
            golem.Inventory.AddInput(ItemType.Scrap, 4);
            golem.Inventory.AddOutput(ItemType.Brass, GolemInventory.CapacityPerType);

            Run(golem, 5);

            Assert.AreEqual(StallReason.OutputFull, golem.StallReason);
            Assert.AreEqual(ItemType.Brass, golem.StallResourceId,
                "progression-design section 8 requires the blocked item type to be named");
            Assert.AreEqual(4, golem.Inventory.GetInput(ItemType.Scrap),
                "a blocked assemble consumed the precursor anyway");
        }

        [Test]
        public void Assemble_WithoutAnAuthoredRecipe_StallsUnconfigured()
        {
            // §1.3: "no recipe" is now the only way an Assemble card can be unfinished -- the
            // card's own inputItemType/outputItemType are no longer part of the answer.
            GolemEntity golem = CreateGolem(Step(AppendageActionType.Assemble));

            golem.Tick(0);

            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
        }

        [Test]
        public void Assemble_UsesItsRecipesDurationRatherThanADerivedOne()
        {
            // The duration comes off the RECIPE, not the card: one Assemble card pointed at a
            // 12-tick recipe and a 90-tick one must not run both at the same speed. AssembleStep
            // leaves an absurd durationTicks on the card to prove it is not what is read.
            GolemEntity golem = CreateGolem(AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 7));
            golem.Inventory.AddInput(ItemType.Scrap, 1);

            Assert.AreEqual(7, TicksToFirstCompletion(golem));
        }

        [Test]
        public void AFullProcessingProgram_HaulAssemblePush_RunsEndToEnd()
        {
            // N x Haul + 1 x Assemble + 1 x Push -- the shape every processing program in the
            // progression design has.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 50);
            var destination = new StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(
                Step(AppendageActionType.Haul, ItemType.Scrap),
                AssembleStep(ItemType.Scrap, ItemType.Brass, durationTicks: 3),
                Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 1);

            for (int tick = 0; tick < 100; tick++)
            {
                golem.Tick(tick);
                Assert.AreNotEqual(GolemState.Stalled, golem.Program.State,
                    "jammed at tick " + tick + " with " + golem.StallReason);
            }

            Assert.Greater(destination.GetQuantity(ItemType.Brass), 5);
            Assert.AreEqual(0, destination.GetQuantity(ItemType.Scrap),
                "raw material leaked out through the Push");
        }

        // --- Buffer backpressure (progression-design §5.3(c), §10, §11 item 3) -----------------
        // Buffers gained a PER-ITEM-TYPE capacity, which is what finally makes "destination
        // full" stalls possible. Everything here is about the one hazard that comes with it:
        // per-type capacity is only a deadlock fix if a refusal of one good never stops
        // another good moving.
        //
        // The two goods below stand in for the §5.3(c) pair until §1.5 authors the real item
        // types -- this is about the SHAPE (a wanted product and a forced byproduct sharing one
        // destination tile), not about the specific goods.
        private const string ProductGood = ItemType.Brass;   // stands in for Iron Plate
        private const string ByproductGood = ItemType.Aether; // stands in for Slag

        [Test]
        public void Push_AFullSlotForOneGood_StillDeliversEveryOtherGoodInTheSameHold()
        {
            // THE regression test for the permanent factory deadlock. If a destination refusing
            // one item type made the golem abandon the rest of its hold, then a backed-up
            // byproduct would stop the main product moving too -- and because a golem's program
            // is rigid, nothing the player can build would ever clear it. Per-item-type capacity
            // is worthless without this: the whole point is that a full slot for one good must
            // never block another.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("Dest", 5);
            destination.Deposit(ByproductGood, 5); // its slot for this good is now full
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            // Blocked type FIRST in the drain order, which is the case the old code broke on:
            // it gave up on the whole push at the first refusal.
            golem.Inventory.AddInput(ByproductGood, 3);
            golem.Inventory.AddInput(ItemType.Scrap, 2);
            golem.Inventory.AddInput(ProductGood, 1);

            golem.Tick(0);

            Assert.AreEqual(2, destination.GetQuantity(ItemType.Scrap),
                "a refused good aborted the push and the goods behind it never moved");
            Assert.AreEqual(1, destination.GetQuantity(ProductGood),
                "a refused good aborted the push and the goods behind it never moved");
            Assert.AreEqual(5, destination.GetQuantity(ByproductGood),
                "the full slot was overrun");
            Assert.AreEqual(3, golem.Inventory.GetInput(ByproductGood),
                "the refused units must stay in the hold, not be dropped");
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(StallReason.None, golem.StallReason,
                "a partial push is progress, not a stall");
        }

        [Test]
        public void Push_PartiallyRefusedByABuffer_IsChargedOnlyForTheUnitsThatLeft()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("Dest", 1);
            destination.Deposit(ByproductGood, 1);
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push, durationTicks: 40));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ByproductGood, 4); // all refused
            golem.Inventory.AddInput(ItemType.Scrap, 1); // accepted

            Assert.AreEqual(3, TicksToFirstCompletion(golem), "2 + the 1 unit that actually left");
        }

        [Test]
        public void Push_WhenEveryGoodInTheHoldIsAtTheDestinationsCap_StallsNamingTheDestination()
        {
            // The other side of the line: a push that can move NOTHING is a real stall, and
            // "ScrapBuffer full" reads correctly for a buffer even though the reason is still
            // spelled BeltFull (StallReason is append-only and serialized by index).
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var destination = new StorageBuffer("ScrapBuffer", 5);
            destination.Deposit(ItemType.Scrap, 5);
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.BeltFull, golem.StallReason);
            Assert.AreEqual("ScrapBuffer", golem.StallResourceId);
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap), "the hold leaked");
            Assert.AreEqual(5, destination.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void ABackedUpByproduct_EventuallyStallsTheSmelterOnOutputFull_RatherThanBeingDropped()
        {
            // progression-design §5.3(c), the whole Slag economy, end to end:
            //   the destination's byproduct slot is full
            //     -> Push delivers the product and the byproduct stays in the golem's hold
            //     -> the hold fills to the golem's own per-type cap over successive cycles
            //     -> the next Assemble of the byproduct stalls OutputFull NAMING IT.
            // "The smelter stalls -- and with it iron, gears, casings and every branch below."
            // Nothing was added to make this happen; it falls out of Push skipping the refused
            // type plus BeginAssemble's existing check-output-room-before-consuming ordering.
            //
            // §1.3 UPDATE: this used to run two single-output Assemble steps as a stand-in,
            // because AppendageActionDefinition carried exactly one output type. It is now the
            // real thing -- ONE recipe (2 Scrap -> 1 Product + 1 Byproduct) whose byproduct is
            // what backs up. The cycle is one tick shorter as a result (one Assemble, not two),
            // which changes nothing the assertions depend on: they are all about the terminal
            // state, and 150 ticks still reaches it either way.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 500);

            const int destinationCap = 20;
            var destination = new StorageBuffer("Dest", destinationCap);
            destination.Deposit(ByproductGood, destinationCap); // the byproduct slot is full

            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(
                Step(AppendageActionType.Haul, ItemType.Scrap),
                AssembleStep(ItemType.Scrap, ProductGood, durationTicks: 1, inputQuantity: 2,
                    byproductItemType: ByproductGood),
                Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Program.SetQuantityAt(0, 2); // the recipe's input quantity

            // Long enough to run well past the golem's 12-per-type output cap: each cycle is
            // Haul(2) + Assemble(1) + Push(2 + 1 delivered) = 6 ticks, so the byproduct hold
            // reaches 12 after 12 cycles (~72 ticks) and the 13th cycle stalls. The stall is
            // terminal, so the exact tick it happens on does not matter here.
            Run(golem, 150);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State,
                "the smelter kept running with nowhere to put its byproduct");
            Assert.AreEqual(StallReason.OutputFull, golem.StallReason);
            Assert.AreEqual(ByproductGood, golem.StallResourceId,
                "the stall must name the blocked byproduct -- that is the player's only clue " +
                "that a disposal route is what the whole line is waiting on");

            Assert.AreEqual(GolemInventory.CapacityPerType, golem.Inventory.GetOutput(ByproductGood),
                "the byproduct was silently dropped instead of backing up");
            Assert.AreEqual(destinationCap, destination.GetQuantity(ByproductGood),
                "the destination's full slot was overrun");

            // And the product line genuinely kept flowing until the hold filled -- a full
            // byproduct slot must not have blocked the product on the way there.
            Assert.AreEqual(GolemInventory.CapacityPerType, destination.GetQuantity(ProductGood),
                "the full byproduct slot blocked the product too, which is the §10 deadlock");
        }

        // --- The id-routed half of the fork, pinned ---------------------------------------------
        // Main.unity's seven demo golems never call ConfigureSpatial. None of the above may
        // reach them, including the derived durations.

        [Test]
        public void IdRouted_HaulKeepsItsAuthoredDurationRatherThanADerivedOne()
        {
            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul, durationTicks: 5));
            golem.Program.SetQuantityAt(0, 1);

            Assert.AreEqual(5, TicksToFirstCompletion(golem),
                "an id-routed Haul picked up the spatial max(2, qty) duration");
        }

        [Test]
        public void IdRouted_ExtractKeepsItsAuthoredDurationAndTouchesNoInternalStock()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            conveyor.System.Register(new BeltSegment("ScrapBeltA", 8));
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, ResourceNode.Infinite));

            var step = Step(AppendageActionType.ExtractFromNode, durationTicks: 1);
            step.sourceId = "ScrapNode";
            step.destinationId = "ScrapBeltA";

            GolemEntity golem = CreateGolem(step);
            golem.Configure("Demo", conveyor);
            golem.ConfigureEconomy(nodes, null);

            Assert.AreEqual(1, TicksToFirstCompletion(golem),
                "an id-routed extract picked up the spatial 6 + qty duration");
            Assert.AreEqual(0, golem.Inventory.Input.TotalUnits,
                "an id-routed extract filled the internal stock instead of the belt");
        }
    }
}
