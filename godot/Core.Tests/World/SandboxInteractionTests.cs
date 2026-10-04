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

        private static SandboxSetup.NodeEntry Stall(SandboxWorld world, string id) =>
            world.Setup.nodes.Single(n => n.id == id);

        [Test]
        public void AtTheFreeScrapStall_EOffersAHarvest_AndHarvests()
        {
            SandboxWorld world = Compose();
            SandboxSetup.NodeEntry stall = Stall(world, "ScrapNode");
            StandAt(world, new Vector2Int(stall.x, stall.y));

            Assert.AreEqual(InteractionAffordance.Ready, world.Interactor.CurrentAffordance);
            StringAssert.Contains("[E]", world.Interactor.CurrentPrompt);
            StringAssert.Contains("Scrap", world.Interactor.CurrentPrompt);

            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(1, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap));
        }

        [Test]
        public void AtAnEmptyStall_EOrdersATruckload_WhenTheWalletCovers()
        {
            SandboxWorld world = Compose();
            SandboxSetup.NodeEntry coal = Stall(world, "CoalNode");
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 10);
            StandAt(world, new Vector2Int(coal.x, coal.y));

            Assert.AreNotEqual(InteractionAffordance.Hidden, world.Interactor.CurrentAffordance);
            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(0, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap), "the 10 Scrap price was paid");
        }

        [Test]
        public void HarvestingRaisesAGainPopupAtTheStall()
        {
            SandboxWorld world = Compose();
            var popups = new List<InteractionPopup>();
            world.Interactor.PopupRaised += popups.Add;
            SandboxSetup.NodeEntry stall = Stall(world, "ScrapNode");
            StandAt(world, new Vector2Int(stall.x, stall.y));

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
        public void APlacedDepotBecomesInteractable_AndERelabelsIt()
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
            string before = part.FilterLabel;
            Assert.IsTrue(world.Interactor.Interact());
            Assert.AreNotEqual(before, part.FilterLabel);
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
