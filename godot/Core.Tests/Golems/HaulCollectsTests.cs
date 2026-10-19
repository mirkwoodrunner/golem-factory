using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The collecting Haul (docs/onboarding-feedback-design.md §13, the user's call, 2026-10-19):
    // a Haul takes only what the golem still needs to hold its batch, stays on its step while
    // short, and advances once the batch is held. Extra stays in the source.
    public class HaulCollectsTests
    {
        private static readonly Vector2Int Behind = new Vector2Int(0, 0);
        private static readonly Vector2Int Golem = new Vector2Int(0, 1);
        private static readonly Vector2Int InFront = new Vector2Int(0, 2);

        private static AppendageActionDefinition Card(AppendageActionType type) =>
            new AppendageActionDefinition { actionType = type, durationTicks = 1 };

        private static AppendageActionDefinition Assemble(string input, int quantity, string output)
        {
            var recipe = new RecipeDefinition { outputItemType = output, outputQuantity = 1, durationTicks = 2 };
            recipe.inputs.Add(new RecipeIngredient(input, quantity));
            return new AppendageActionDefinition { actionType = AppendageActionType.Assemble, recipe = recipe };
        }

        private static GolemEntity Placed(SpatialEndpointRegistry endpoints, params AppendageActionDefinition[] cards)
        {
            var golem = new GolemEntity();
            golem.Configure("Collector", null);
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            foreach (AppendageActionDefinition card in cards)
            {
                golem.Program.appendages.Add(card);
            }
            golem.ConfigureSpatial(endpoints, Golem, Facing.North);
            return golem;
        }

        private static GolemEntity Hauler(SpatialEndpointRegistry endpoints, int batch, params AppendageActionDefinition[] after)
        {
            var cards = new AppendageActionDefinition[after.Length + 1];
            cards[0] = Card(AppendageActionType.Haul);
            after.CopyTo(cards, 1);
            GolemEntity golem = Placed(endpoints, cards);
            golem.Program.SetItemTypeAt(0, ItemType.Scrap);
            golem.Program.SetQuantityAt(0, batch);
            return golem;
        }

        private static long Run(GolemEntity golem, long from, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                golem.Tick(from++);
            }
            return from;
        }

        [Test]
        public void AShortCrate_IsTakenFromAndTheStepStays_ThenArrivalsTopItUpToExactlyTheBatch()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Scrap, 2);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Hauler(endpoints, 5);

            golem.Tick(0);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(2, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(0, crate.GetQuantity(ItemType.Scrap));
            Assert.IsTrue(golem.TryGetHaulProgress(out string good, out int held, out int batch));
            Assert.AreEqual((ItemType.Scrap, 2, 5), (good, held, batch));

            golem.Tick(1);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State, "it advanced on a partial batch");

            crate.Deposit(ItemType.Scrap, 10);
            golem.Tick(2);
            Assert.AreEqual(5, golem.Inventory.GetInput(ItemType.Scrap), "it took more than the batch needed");
            Assert.AreEqual(7, crate.GetQuantity(ItemType.Scrap), "the extra must stay in the crate");
            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.IsFalse(golem.TryGetHaulProgress(out _, out _, out _));
        }

        [Test]
        public void TheOldJam_ASmelterHandFedOneScrapAtATime_AssemblesOnceTwoAreHeld()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            var output = new StorageBuffer("Output");
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            endpoints.Register(InFront, new StorageBufferEndpoint(output));
            GolemEntity golem = Hauler(endpoints, 2,
                Assemble(ItemType.Scrap, 2, ItemType.IronPlate), Card(AppendageActionType.Push));

            crate.Deposit(ItemType.Scrap, 1);
            long tick = Run(golem, 0, 40);
            Assert.AreEqual(0, golem.Program.CurrentStepIndex, "it left the Haul with one Scrap of two");
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(1, golem.StallShortfall);

            crate.Deposit(ItemType.Scrap, 1);
            Run(golem, tick, 60);
            Assert.AreEqual(1, output.GetQuantity(ItemType.IronPlate), "the smelter still jammed");
        }

        [Test]
        public void AOneSlotBelt_FeedingABatchOfFive_Completes()
        {
            // A placed belt offers one good at a time, so it could never hold a batch: the reason
            // the first draft (wait until the source holds the batch) was withdrawn.
            var endpoints = new SpatialEndpointRegistry();
            var belt = new BeltSegment("Belt", 1);
            var output = new StorageBuffer("Output");
            endpoints.Register(Behind, new BeltSegmentEndpoint(belt));
            endpoints.Register(InFront, new StorageBufferEndpoint(output));
            GolemEntity golem = Hauler(endpoints, 5, Card(AppendageActionType.Push));

            int fed = 0;
            for (long tick = 0; tick < 200 && output.GetQuantity(ItemType.Scrap) < 5; tick++)
            {
                if (fed < 5 && belt.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap }))
                {
                    fed++;
                }
                belt.Advance(1f);
                golem.Tick(tick);
            }

            Assert.AreEqual(5, output.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void AHoldAlreadyOverTheBatch_TakesNothingAndRuns()
        {
            // A lowered dial, or a recipe's remainder: the Haul has nothing to collect.
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Scrap, 10);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Hauler(endpoints, 2, Assemble(ItemType.Scrap, 2, ItemType.IronPlate));
            golem.Inventory.AddInput(ItemType.Scrap, 5);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.AreEqual(10, crate.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(5, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void TwoHaulsOfTheSameGood_DoNotAdd()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Scrap, 100);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Hauler(endpoints, 2, Card(AppendageActionType.Haul));
            golem.Program.SetItemTypeAt(1, ItemType.Scrap);
            golem.Program.SetQuantityAt(1, 3);

            Run(golem, 0, 40);

            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(97, crate.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void ALogisticsGolem_CollectsItsBatchBeforeItPushes()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            var output = new StorageBuffer("Output");
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            endpoints.Register(InFront, new StorageBufferEndpoint(output));
            GolemEntity golem = Hauler(endpoints, 4, Card(AppendageActionType.Push));

            crate.Deposit(ItemType.Scrap, 3);
            long tick = Run(golem, 0, 30);
            Assert.AreEqual(0, output.GetQuantity(ItemType.Scrap), "it pushed a partial batch");

            crate.Deposit(ItemType.Scrap, 1);
            Run(golem, tick, 30);
            Assert.AreEqual(4, output.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void ExtractFromNode_StillTakesAPartialBatch()
        {
            var endpoints = new SpatialEndpointRegistry();
            endpoints.Register(Behind, new ResourceNodeEndpoint(new ResourceNode("Stall", ItemType.Scrap, 2)));
            GolemEntity golem = Placed(endpoints, Card(AppendageActionType.ExtractFromNode));
            golem.Program.SetQuantityAt(0, 8);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(2, golem.Inventory.GetInput(ItemType.Scrap));
        }
    
        // --- Phase 2 critique: the edges of "stay on the step until the batch is held" -------

        [Test]
        public void ABatchLoweredMidCollect_RunsOnWhatIsHeld()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Scrap, 3);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Hauler(endpoints, 5);
            golem.Tick(0);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State);

            golem.Program.SetQuantityAt(0, 3);
            golem.Tick(1);
            Assert.AreEqual(GolemState.Running, golem.Program.State, "a lowered dial must not leave it collecting");
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void CollectProgress_IsDerivedFromTheHold_SoALoadMidCollectResumesIt()
        {
            // A save keeps the hold and the step index, not the stall: a golem restored holding
            // 3 of a batch of 5 collects the 2 it lacks and no more.
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Scrap, 1);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Hauler(endpoints, 5);
            golem.Inventory.AddInput(ItemType.Scrap, 3);

            golem.Tick(0);
            Assert.IsTrue(golem.TryGetHaulProgress(out _, out int held, out int batch));
            Assert.AreEqual((4, 5), (held, batch));
            crate.Deposit(ItemType.Scrap, 5);
            golem.Tick(1);
            Assert.AreEqual(5, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(4, crate.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void ALabelledCrate_GivesOnlyItsGood_AndAHaulOfAnotherStaysPut()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate");
            crate.Deposit(ItemType.Coal, 5);
            endpoints.Register(Behind, new FilteredBufferEndpoint(crate, ItemType.Coal));
            GolemEntity golem = Hauler(endpoints, 2, Card(AppendageActionType.Push));

            golem.Tick(0);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Coal), "a Scrap Haul took Coal");
            Assert.AreEqual(0, golem.Program.CurrentStepIndex, "rigid: it never skips to the Push");

            golem.Program.SetItemTypeAt(0, ItemType.Coal);
            golem.Tick(1);
            Assert.AreEqual(2, golem.Inventory.GetInput(ItemType.Coal));
            Assert.AreEqual(3, crate.GetQuantity(ItemType.Coal));
        }
}
}
