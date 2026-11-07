using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.Tests.Data;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// The player's [E] in the composed Sandbox: what is advertised standing next to each kind
    /// of thing, and what pressing it does. Found missing in the G5 hands-on check -- the carts
    /// and the bench answered nothing, because the Godot player still ran the spike's own
    /// interaction code instead of Core's PlayerInteractor.
    /// </summary>
    public class SandboxInteractionTests
    {
        private static SandboxWorld Compose()
        {
            var definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        private static void StandAt(SandboxWorld world, Vector2Int cell, float dy = 1f)
        {
            world.Interactor.Position = new Vector3(cell.x, cell.y + dy, 0f);
            world.Interactor.Poll();
        }

        /// <summary>Where a good's first cart stands: the starting Scrap cart, or the spot the guide marks.</summary>
        private static Vector2Int Home(SandboxWorld world, string item) => world.Setup.HomeSpot(item).Value;

        [Test]
        public void AtTheStartingScrapCart_EOffersAHarvest_AndHarvests()
        {
            SandboxWorld world = Compose();
            StandAt(world, Home(world, ItemType.Scrap));

            Assert.AreEqual(InteractionAffordance.Ready, world.Interactor.CurrentAffordance);
            StringAssert.StartsWith("[E]  Harvest Scrap cart  -  200 of 200", world.Interactor.CurrentPrompt);

            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(1, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap));
        }

        // docs/supply-design.md §2: an empty cart has nothing to take, and nothing to order;
        // its line says how it fills and that [I] opens it.
        [Test]
        public void AnEmptyCart_GreysItsHarvest_AndSaysHowItFills()
        {
            SandboxWorld world = Compose();
            Assert.IsTrue(world.GrantCart(Home(world, ItemType.Coal), ItemType.Coal).Done);
            StandAt(world, Home(world, ItemType.Coal));

            Assert.AreEqual(InteractionAffordance.Unavailable, world.Interactor.CurrentAffordance);
            StringAssert.Contains("Coal cart  -  0 of 100", world.Interactor.CurrentPrompt);
            StringAssert.Contains("+30 a minute", world.Interactor.CurrentPrompt);
            StringAssert.Contains("[I] open", world.Interactor.CurrentPrompt);
            StringAssert.DoesNotContain("truckload", world.Interactor.CurrentPrompt);
            Assert.IsFalse(world.Interactor.Interact(), "nothing to take");
        }

        [Test]
        public void ACartWithStock_OffersAHarvest_UnderItsEnglishName()
        {
            SandboxWorld world = Compose();
            Vector2Int copper = Home(world, ItemType.CopperOre);
            Assert.IsTrue(world.GrantCart(copper, ItemType.CopperOre).Done);
            world.Supply.SpotAt(copper).Cart.Node.Deliver(10);
            StandAt(world, copper);

            StringAssert.StartsWith("[E]  Harvest Copper Ore cart  -  10 of 100", world.Interactor.CurrentPrompt);
        }

        [Test]
        public void HoldingE_AtTheScrapCart_KeepsTaking_WhileItRefills()
        {
            // At 60 frames a second, as the game holds it: each repeat was once worked out from the
            // frame before, stayed an interval ahead, and the hold took two goods and stopped.
            SandboxWorld world = Compose();
            world.Clock.Play();
            StandAt(world, Home(world, ItemType.Scrap));
            Assert.IsTrue(world.Interactor.Interact());
            for (int frame = 0; frame < 180; frame++)
            {
                world.Advance(1f / 60f);
                world.Interactor.Poll();
                world.Interactor.HoldHarvest(1f / 60f, true, false);
            }
            Assert.GreaterOrEqual(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap), 18, "three seconds held: one, then one every 0.15 s");
        }

        [Test]
        public void HarvestingRaisesAGainPopupAtTheStall()
        {
            SandboxWorld world = Compose();
            var popups = new List<InteractionPopup>();
            world.Interactor.PopupRaised += popups.Add;
            Vector2Int stall = Home(world, ItemType.Scrap);
            StandAt(world, stall);

            world.Interactor.Interact();

            Assert.AreEqual(1, popups.Count);
            Assert.AreEqual(InteractionPopupKind.Gain, popups[0].Kind);
            Assert.AreEqual(stall.x, popups[0].Position.x, 1e-4);
        }

        [Test]
        public void TheStarterBenchIsWithinReach_AndHoldingECranksIt()
        {
            SandboxWorld world = Compose();
            StandAt(world, SandboxSetup.CellOf(world.Setup.starterBench), dy: -1f);

            Assert.AreSame(world.StarterBench, world.Interactor.NearestBench, "the bench the HUD readout describes");
            world.Interactor.SetInteractHeld(true);
            world.Interactor.Poll();
            Assert.IsTrue(world.StarterBench.IsCranking);

            world.Interactor.SetInteractHeld(false);
            world.Interactor.Poll();
            Assert.IsFalse(world.StarterBench.IsCranking);
        }

        [Test]
        public void TheStarterStationIsAdvertised()
        {
            SandboxWorld world = Compose();
            StandAt(world, SandboxSetup.CellOf(world.Setup.starterStation), dy: -1f);
            Assert.AreEqual(InteractionKind.Construct, world.Interactor.CurrentPick.Kind);
            Assert.AreNotEqual("", world.Interactor.CurrentPrompt);
        }

        [Test]
        public void APlacedDepotBecomesInteractable_AndECollectsFromItsCrate()
        {
            SandboxWorld world = Compose();
            var depot = world.Placeables.Single(p => p.Key == "DepotPrefab");
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 15);
            world.Build.SetActivePrefab(depot.Prefab);
            var cell = new Vector2Int(6, 6);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();

            StandAt(world, cell);
            Assert.AreEqual(InteractionKind.Sort, world.Interactor.CurrentPick.Kind, "the world told the interactor about the new building");
            PlaceableDepot part = world.Build.Buildings.Single().GetPart<PlaceableDepot>();
            Assert.AreEqual(InteractionAffordance.Unavailable, world.Interactor.CurrentAffordance, "an empty crate has nothing to collect");
            part.Buffer.Deposit(ItemType.Coke, 7);
            world.Interactor.Poll();
            StringAssert.Contains("[E]  Collect from Crate  -  holds 7 Coke", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(0, part.Buffer.GetQuantity(ItemType.Coke));
            Assert.AreEqual(7, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Coke));
        }

        [Test]
        public void AFreshlyBuiltGolem_IsWithinReach_AndGCarriesIt()
        {
            // The station announces "interactables changed" before the world has added the golem
            // to its roster; the world must refresh again after, or the golem is unreachable.
            SandboxWorld world = Compose();
            var scavenger = world.Definitions.Chassis["ClockworkScavenger"];
            foreach (var c in scavenger.cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity);
            }
            Assert.IsTrue(world.StarterStation.TryConstructGolem(scavenger, out var golem));

            world.Interactor.Position = new Vector3(golem.Cell.x, golem.Cell.y - 1f, 0f);
            world.Interactor.Poll();

            Assert.IsTrue(world.Interactor.ToggleCarryGolem(), world.Interactor.LastStatusMessage);
            Assert.AreSame(golem, world.Interactor.CarriedGolem);
        }

        [Test]
        public void NothingNearby_NothingAdvertised()
        {
            SandboxWorld world = Compose();
            world.Interactor.Position = new Vector3(-9f, 9f, 0f);
            world.Interactor.Poll();
            Assert.AreEqual("", world.Interactor.CurrentPrompt);
        }
    }
}
