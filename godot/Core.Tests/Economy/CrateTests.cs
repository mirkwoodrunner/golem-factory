using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;
using Vector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Crates and the pack (docs/crates-design.md §9, steps 1 and 2), through the real Sandbox:
    /// a depot holds its own goods, a golem reaches only the crates beside it, the pack is capped,
    /// E collects what fits, and demolition refunds a crate's contents or refuses.
    /// </summary>
    public class CrateTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        private static int Pack(SandboxWorld world, string item) => world.Buffers.GetQuantity(world.StockpileBufferId, item);

        private static void Give(SandboxWorld world, string item, int quantity) =>
            world.Buffers.Deposit(world.StockpileBufferId, item, quantity);

        private static PlaceableBuilding Place(SandboxWorld world, string key, Vector2Int cell)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell);
        }

        private static void StandAt(SandboxWorld world, Vector2Int cell)
        {
            world.Interactor.Position = new Vector3(cell.x, cell.y + 1f, 0f);
            world.Interactor.Poll();
        }

        // --- A depot holds its own goods ------------------------------------------------------

        [Test]
        public void APlacedDepot_PublishesItsOwnFiftyCrate_NamedByItsCell()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 30);
            var cell = new Vector2Int(4, 4);
            ICrate crate = Place(world, "DepotPrefab", cell).GetPart<ICrate>();

            Assert.AreEqual("Crate(4,4)", crate.CrateId);
            Assert.IsTrue(crate.OwnsBuffer);
            Assert.AreEqual(50, crate.Buffer.CapacityPerType);
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint));
            Assert.IsTrue(endpoint.TryGive(new GolemFactory.Belts.ItemStack { ItemType = ItemType.Coke }));
            Assert.AreEqual(1, crate.Buffer.GetQuantity(ItemType.Coke), "a push lands in the crate");
            Assert.AreEqual(0, Pack(world, ItemType.Coke), "and never in the pack");
            Assert.AreEqual(100, world.Wallet.Pack.CapacityPerType, "the pack carries 100 of each");
        }

        [Test]
        public void ADepotWithAnExplicitBufferId_PublishesThatBuffer_AndDoesNotOwnIt()
        {
            // Opt-in by null (crates-design §0.10): a named buffer is a shared store, a Post crate.
            var depot = new PlaceableDepot();
            depot.Configure("SharedStore");
            var endpoints = new SpatialEndpointRegistry();
            var buffers = new StorageBufferRegistry();
            Assert.IsTrue(depot.RegisterAsSpatialEndpoint(endpoints, buffers, new Vector2Int(1, 1)));

            Assert.AreEqual("SharedStore", depot.CrateId);
            Assert.IsFalse(depot.OwnsBuffer);
            Assert.AreSame(buffers.GetOrCreate("SharedStore"), depot.Buffer);
        }

        [Test]
        public void AConverterBehindAnEmptyCrate_Stalls_EvenWithThePackFull()
        {
            // Golems never reach the pack (crates-design §0.4): a Presser behind an empty crate
            // waits, naming the crate, however much Scrap the player carries.
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            Give(world, ItemType.Scrap, 100);
            Give(world, ItemType.IronPlate, 100);
            Give(world, ItemType.Gear, 100);
            Give(world, ItemType.Coke, 100);
            PlaceableBoiler boiler = Place(world, "BoilerPrefab", new Vector2Int(5, 5)).GetPart<PlaceableBoiler>();
            world.Interactor.TryRefuelBoiler(boiler);
            ICrate behind = Place(world, "DepotPrefab", new Vector2Int(4, 4)).GetPart<ICrate>();
            Place(world, "DepotPrefab", new Vector2Int(4, 6));
            Give(world, ItemType.Scrap, 100);

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity presser));
            presser.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            presser.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            presser.Program.TryAddAppendage(defs.Appendages["AssembleScrapReclamation"]);
            presser.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            presser.SetPlacement(new Vector2Int(4, 5), Facing.North);
            Give(world, ItemType.Scrap, 100);
            int packScrap = Pack(world, ItemType.Scrap);

            world.Clock.Play();
            for (int frame = 0; frame < 120; frame++)
            {
                world.Advance(1f / 30f);
            }

            Assert.AreEqual(GolemState.Stalled, presser.Program.State);
            Assert.AreEqual(StallReason.BufferEmpty, presser.StallReason);
            Assert.AreEqual("Crate(4,4)", presser.StallResourceId, "the badge names the crate");
            Assert.AreEqual(packScrap, Pack(world, ItemType.Scrap), "the pack is out of its reach");

            // Fed by hand, it runs.
            Assert.AreEqual(10, CrateTransfer.Put(world.Wallet.Pack, behind, ItemType.Scrap, 10).Moved);
            for (int frame = 0; frame < 120; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreNotEqual(StallReason.BufferEmpty, presser.StallReason, "Scrap in the crate: it hauls");
            Assert.Less(behind.Buffer.GetQuantity(ItemType.Scrap), 10);
        }

        // --- Collect ------------------------------------------------------------------------------

        [Test]
        public void ECollect_MovesWhatFits_AndLeavesTheRestInTheCrate()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            var cell = new Vector2Int(6, 6);
            ICrate crate = Place(world, "DepotPrefab", cell).GetPart<ICrate>();
            Give(world, ItemType.Scrap, 95);
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            crate.Buffer.Deposit(ItemType.Coke, 3);

            StandAt(world, cell);
            Assert.AreEqual(InteractionKind.Sort, world.Interactor.CurrentPick.Kind);
            StringAssert.Contains("holds 20 Scrap, 3 Coke", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);

            Assert.AreEqual(100, Pack(world, ItemType.Scrap), "the pack fills to its cap");
            Assert.AreEqual(15, crate.Buffer.GetQuantity(ItemType.Scrap), "the rest stays in the crate");
            Assert.AreEqual(3, Pack(world, ItemType.Coke), "every good in one press");
            Assert.AreEqual(0, crate.Buffer.GetQuantity(ItemType.Coke));

            world.Interactor.Poll();
            Assert.AreEqual(InteractionAffordance.Unavailable, world.Interactor.CurrentAffordance, "nothing left fits");
            StringAssert.Contains("pack full", world.Interactor.CurrentPrompt);
            Assert.IsFalse(world.Interactor.Interact());
            StringAssert.Contains("Pack full of Scrap", world.Interactor.LastStatusMessage);
            Assert.AreEqual(15, crate.Buffer.GetQuantity(ItemType.Scrap), "a refused collect moves nothing");
        }

        [Test]
        public void ATransfer_HonoursALabelOnTheWayIn_NeverOnTheWayOut_AndLosesNothing()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            ICrate crate = Place(world, "DepotPrefab", new Vector2Int(6, 6)).GetPart<ICrate>();
            crate.Buffer.Deposit(ItemType.Scrap, 5);
            crate.SetFilter(ItemType.CopperOre);
            Give(world, ItemType.Scrap, 30);
            int total = Pack(world, ItemType.Scrap) + crate.Buffer.GetQuantity(ItemType.Scrap);

            TransferResult refused = CrateTransfer.Put(world.Wallet.Pack, crate, ItemType.Scrap, 10);
            Assert.AreEqual(0, refused.Moved);
            Assert.AreEqual("This crate is for Copper Ore", refused.Refusal);

            TransferResult taken = CrateTransfer.Take(crate, world.Wallet.Pack, ItemType.Scrap, int.MaxValue);
            Assert.AreEqual(5, taken.Moved, "the player's hands take anything out");
            Assert.AreEqual(total, Pack(world, ItemType.Scrap) + crate.Buffer.GetQuantity(ItemType.Scrap));

            Give(world, ItemType.CopperOre, 60);
            TransferResult put = CrateTransfer.Put(world.Wallet.Pack, crate, ItemType.CopperOre, CrateTransfer.AmountFor(shift: false));
            Assert.AreEqual(50, put.Moved, "all that fits: the crate's 50");
            Assert.AreEqual(10, Pack(world, ItemType.CopperOre));
            Assert.AreEqual("Crate full of Copper Ore", CrateTransfer.Put(world.Wallet.Pack, crate, ItemType.CopperOre, 1).Refusal);
        }

        // --- Harvest -------------------------------------------------------------------------------

        [Test]
        public void HarvestStopsAtThePacksCap_AndTheStallKeepsItsUnit()
        {
            SandboxWorld world = Compose();
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Assert.IsTrue(world.Nodes.TryGetNode("ScrapNode", out ResourceNode node));
            Give(world, ItemType.Scrap, 100);
            int stock = node.RemainingQuantity;

            StandAt(world, new Vector2Int(stall.x, stall.y));
            Assert.AreEqual(InteractionKind.Harvest, world.Interactor.CurrentPick.Kind);
            Assert.AreEqual(InteractionAffordance.Unavailable, world.Interactor.CurrentAffordance);
            StringAssert.Contains("pack full", world.Interactor.CurrentPrompt);

            Assert.IsFalse(world.Interactor.Interact());
            Assert.AreEqual(stock, node.RemainingQuantity, "the stall keeps the unit: nothing is lost");
            Assert.AreEqual(100, Pack(world, ItemType.Scrap));

            Assert.IsTrue(world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 1) }));
            Assert.IsTrue(world.Interactor.Interact(), "room for one: one harvested");
            Assert.AreEqual(stock - 1, node.RemainingQuantity);
        }

        // --- Demolition --------------------------------------------------------------------------

        [Test]
        public void DemolishingACrate_RefundsItsCostAndContents_AndARebuiltOneStartsEmpty()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            var cell = new Vector2Int(6, 6);
            ICrate crate = Place(world, "DepotPrefab", cell).GetPart<ICrate>();
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            crate.Buffer.Deposit(ItemType.Gear, 4);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();

            Assert.IsFalse(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == cell), "demolished");
            Assert.AreEqual(35, Pack(world, ItemType.Scrap), "15 cost and 20 contents");
            Assert.AreEqual(4, Pack(world, ItemType.Gear));
            Assert.IsFalse(world.Buffers.TryGetBuffer("Crate(6,6)", out _), "the crate went with its depot");
            Assert.IsFalse(world.Endpoints.TryGetEndpoint(cell, out _));

            ICrate again = Place(world, "DepotPrefab", cell).GetPart<ICrate>();
            Assert.AreEqual(0, again.Buffer.Quantities.Values.Sum(), "a fresh crate");
        }

        [Test]
        public void DemolishingACrate_WhoseContentsWontFit_IsRefused_AndNothingMoves()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            var cell = new Vector2Int(6, 6);
            PlaceableBuilding depot = Place(world, "DepotPrefab", cell);
            ICrate crate = depot.GetPart<ICrate>();
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            Give(world, ItemType.Scrap, 100);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);

            Assert.IsFalse(depot.IsRemoved, "the crate stands");
            Assert.AreEqual("Not demolished: pack full of Scrap.", world.Build.LastStatusMessage);
            Assert.AreEqual(20, crate.Buffer.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(100, Pack(world, ItemType.Scrap));
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(cell, out _), "still a working crate");

            world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 40) });
            world.Build.PlaceOrRemove(cell);
            Assert.IsTrue(depot.IsRemoved, "room made: demolished");
            Assert.AreEqual(95, Pack(world, ItemType.Scrap), "60 + 15 cost + 20 contents");
        }

        [Test]
        public void ALoadsSweep_RefundsNoCrateContents()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            ICrate crate = Place(world, "DepotPrefab", new Vector2Int(6, 6)).GetPart<ICrate>();
            crate.Buffer.Deposit(ItemType.Scrap, 20);

            world.Build.ClearRuntimePlacedBuildings();

            Assert.AreEqual(0, Pack(world, ItemType.Scrap), "refund: false, or save/load duplicates");
            Assert.IsFalse(world.Buffers.TryGetBuffer("Crate(6,6)", out _));
        }

        [Test]
        public void DismantlingAGolem_IntoAFullPack_IsRefused_NamingTheGood()
        {
            SandboxWorld world = Compose();
            ChassisDefinition scavenger = world.Definitions.Chassis["ClockworkScavenger"];
            Give(world, ItemType.Scrap, 12);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(scavenger, out GolemEntity golem));
            Give(world, ItemType.Scrap, 100);

            Assert.IsFalse(world.StarterStation.TryDismantleGolem(golem, out _, out string reason));
            Assert.AreEqual("Not dismantled: pack full of Scrap.", reason);
            Assert.IsFalse(golem.IsRemoved);
        }

        // --- The wallet ----------------------------------------------------------------------------

        [Test]
        public void TheWallet_ChargesAtomically_PackFirstThenPost_AndRefundsTheSameWay()
        {
            var buffers = new StorageBufferRegistry();
            buffers.SetCapacity("Pack", 100);
            buffers.SetCapacity("Post", 200);
            var wallet = new PlayerWallet(buffers, "Pack", "Post");
            buffers.Deposit("Pack", ItemType.Scrap, 30);
            buffers.Deposit("Post", ItemType.Scrap, 50);
            buffers.Deposit("Post", ItemType.Brass, 5);

            Assert.AreEqual(80, wallet.Quantity(ItemType.Scrap));
            Assert.IsFalse(wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 40), new RecipeIngredient(ItemType.Brass, 6) }));
            Assert.AreEqual(30, buffers.GetQuantity("Pack", ItemType.Scrap), "refused: nothing taken");
            Assert.AreEqual(50, buffers.GetQuantity("Post", ItemType.Scrap));

            Assert.IsTrue(wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 40), new RecipeIngredient(ItemType.Brass, 5) }));
            Assert.AreEqual(0, buffers.GetQuantity("Pack", ItemType.Scrap), "the pack first");
            Assert.AreEqual(40, buffers.GetQuantity("Post", ItemType.Scrap), "then the Post");
            Assert.IsTrue(wallet.TryCharge(null), "free is free");

            Assert.IsTrue(wallet.Refund(new[] { new RecipeIngredient(ItemType.Scrap, 130) }));
            Assert.AreEqual(100, buffers.GetQuantity("Pack", ItemType.Scrap), "the pack first");
            Assert.AreEqual(70, buffers.GetQuantity("Post", ItemType.Scrap), "the rest to the Post");
            Assert.AreEqual(ItemType.Scrap, wallet.FirstMisfit(new[] { new RecipeIngredient(ItemType.Scrap, 131) }));
            Assert.IsFalse(wallet.Refund(new[] { new RecipeIngredient(ItemType.Scrap, 131) }));
            Assert.AreEqual(100, buffers.GetQuantity("Pack", ItemType.Scrap), "a refund that won't fit deposits nothing");
        }

        [Test]
        public void APackAboveItsCap_CanBeSpent_ButNotAddedTo()
        {
            SandboxWorld world = Compose();
            world.Wallet.Pack.RestoreQuantity(ItemType.Scrap, 140);

            Assert.AreEqual(0, world.Wallet.RoomFor(ItemType.Scrap));
            Assert.AreEqual(0, world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 1));
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 30) }));
            Assert.AreEqual(110, Pack(world, ItemType.Scrap));
            Assert.AreEqual(0, world.Wallet.RoomFor(ItemType.Scrap), "still above: still closed");
        }

        // --- Every price fits in a full pack (crates-design §6.1) ---------------------------------

        [Test]
        public void EveryPriceInTheGame_IsPayableFromAFullPack()
        {
            SandboxWorld world = Compose();
            int cap = world.Wallet.Pack.CapacityPerType;
            Assert.AreEqual(100, cap);
            var prices = new List<(string what, IReadOnlyList<RecipeIngredient> price)>();

            prices.AddRange(world.Placeables.Select(p => ("placeable " + p.Key, (IReadOnlyList<RecipeIngredient>)p.Prefab.Cost)));
            prices.AddRange(world.Definitions.Chassis.Values.Select(c => ("chassis " + c.name, (IReadOnlyList<RecipeIngredient>)c.cost)));
            foreach (DraftableCardCatalog deck in world.Definitions.Decks.Values)
            {
                foreach (DraftableCardDefinition card in deck.Cards.Concat(deck.OpeningHand).Where(c => c != null))
                {
                    IReadOnlyList<RecipeIngredient> opening = card.HasBundleCost
                        ? card.claimCost
                        : new[] { new RecipeIngredient(ItemType.Scrap, card.baseCost) };
                    prices.Add(("card " + card.name, opening));
                }
            }
            foreach (string stall in world.Market.StallIds)
            {
                world.Market.TryGetOffer(stall, out MarketOffer offer);
                prices.Add(("truckload " + stall, offer.Price));
            }
            prices.Add(("assembly bays", world.AssemblyBay.UpgradeCost));

            int steps = 0;
            while (world.Bounds.CanExpand)
            {
                prices.Add(("floor expansion " + (++steps), world.FloorExpansion.NextCost()));
                world.Bounds.Expand(world.FloorExpansion.RowsPerPurchase);
            }
            Assert.GreaterOrEqual(steps, 6, "the six purchases of §6.1");

            foreach ((string what, IReadOnlyList<RecipeIngredient> price) in prices)
            {
                foreach (RecipeIngredient good in price ?? new RecipeIngredient[0])
                {
                    Assert.LessOrEqual(good.quantity, cap, $"{what} asks for {good.quantity} {good.itemType}, more than a full pack");
                }
            }
        }

        // --- Step 2: the mast's crate, and the Ledger's rate ---------------------------------------

        [Test]
        public void AFreightMast_LandsDeliveriesInItsOwnCrate_WhichECollects()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Brass, 20);
            Give(world, ItemType.Casing, 10);
            var cell = new Vector2Int(6, 6);
            ICrate mast = Place(world, "FreightMastPrefab", cell).GetPart<ICrate>();

            Assert.AreEqual("Mast(6,6)", mast.CrateId, "the id it registers with the mast network");
            Assert.IsFalse(mast.CanLabel);
            Assert.IsTrue(world.Masts.TryGetId(cell, out string mastId));
            Assert.AreEqual(mast.CrateId, mastId);

            // The Zeppelin's landing is a give into the endpoint on the mast's cell.
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint));
            Assert.IsTrue(endpoint.TryGive(new GolemFactory.Belts.ItemStack { ItemType = ItemType.CopperOre }));
            Assert.AreEqual(1, mast.Buffer.GetQuantity(ItemType.CopperOre));
            Assert.AreEqual(0, Pack(world, ItemType.CopperOre), "not the pack");

            StandAt(world, cell);
            StringAssert.Contains("Collect from Freight Mast", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(1, Pack(world, ItemType.CopperOre));
        }

        [Test]
        public void TheLedgersRate_IsFreshProduction_NotThePack()
        {
            SandboxWorld world = Compose();
            world.LedgerReadout.Select("r2.reclamation");
            for (int second = 0; second < 6; second++)
            {
                EventBus.Publish(new ItemAssembledEvent("RateLine", ItemType.IronPlate, 1, world.Clock.CurrentTick));
                for (int frame = 0; frame < 30; frame++)
                {
                    world.Advance(1f / 30f);
                }
            }

            Assert.IsTrue(world.Throughput.TryGetRatePerMinute(ProductionTally.SeriesId, ItemType.IronPlate, out float rate));
            Assert.AreEqual(60f, rate, 15f, "one a second is about 60 a minute");
            Assert.AreEqual(0, Pack(world, ItemType.IronPlate), "and the pack never saw any of it");
            StringAssert.Contains("now: ", world.LedgerReadout.Body, "the pane shows the live rate");
        }
    }
}
